using Moq;
using SmartPark.Core.Interfaces;
using SmartPark.Core.Models;
using SmartPark.Core.Services;

namespace SmartPark.Tests.IntegrationTests;

public class ParkingFlowIntegrationTests
{
    // ────────────────────────────────────────────────────────────
    //  INTEGRATION TEST SETUP
    //  Uses REAL components for business logic, and TEST DOUBLES
    //  only for external boundaries:
    //
    //  Real objects:
    //    ParkingFeeCalculator       — real (pure logic, no side effects)
    //    InMemoryParkingRepository  — fake (working in-memory implementation)
    //
    //  Test doubles (via Moq, used as stubs here):
    //    IPaymentGateway            — stub (always returns success)
    //    INotificationService       — stub (does nothing)
    //    IDateTimeProvider          — stub (returns controlled time)
    //    IMembershipService         — stub (returns Guest for all)
    // ────────────────────────────────────────────────────────────

    private readonly ParkingFeeCalculator _feeCalculator = new();
    private readonly InMemoryParkingRepository _repository = new();  // fake
    private readonly Mock<IPaymentGateway> _paymentStub = new();
    private readonly Mock<INotificationService> _notificationStub = new();
    private readonly ParkingSessionManager _manager;

    // Fake clock — set this in each test to control time
    private DateTime _currentTime = new(2026, 3, 16, 10, 0, 0); // Monday 10 AM

    public ParkingFlowIntegrationTests()
    {
        var dateTimeStub = new Mock<IDateTimeProvider>();
        dateTimeStub.Setup(d => d.Now).Returns(() => _currentTime);

        var membershipStub = new Mock<IMembershipService>();
        membershipStub.Setup(m => m.GetMembershipTier(It.IsAny<string>())).Returns(MembershipTier.Guest);

        _paymentStub.Setup(p => p.ProcessPaymentAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(true);

        _manager = new ParkingSessionManager(
            _feeCalculator,
            _paymentStub.Object,
            _notificationStub.Object,
            membershipStub.Object,
            _repository,          // real fake, not a Moq object
            dateTimeStub.Object);
    }

    // ────────────────────────────────────────────────────────────
    //  EXAMPLE TEST — shows how to advance time between operations.
    //  Delete or keep this; it does not count toward your grade.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FullFlow_CheckInAndCheckOut_CalculatesCorrectFee()
    {
        // Arrange — check in at 10:00 AM
        _currentTime = new DateTime(2026, 3, 16, 10, 0, 0); // Monday
        var ticket = await _manager.CheckInAsync("TEST-001", VehicleType.Car);

        // Act — check out at 12:30 PM (2.5 hours later → 2 billable hours after grace)
        _currentTime = new DateTime(2026, 3, 16, 12, 30, 0);
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-345-678");

        // Assert — Car: 2 hours × 1,000 = 2,000 KHR
        Assert.Equal(2_000m, result.TotalFee);
    }

    #region Full Parking Flow
    [Fact]
    public async Task FullFlow_LostTicket_CalculatesPenaltyAndStoresState()
    {
        _currentTime = new DateTime(2026, 4, 15, 10, 0, 0); // Wednesday
        var ticket = await _manager.CheckInAsync("TEST-002", VehicleType.Motorcycle);

        _currentTime = new DateTime(2026, 4, 15, 10, 15, 0); // 15 mins (grace period)
        var result = await _manager.CheckOutAsync(ticket.TicketId, "012-345-678", isLostTicket: true);

        // Assert: 0 base fee, but 20k lost ticket penalty.
        Assert.Equal(20_000m, result.TotalFee);
        
        // Verify state
        var dbTicket = await _repository.GetTicketByIdAsync(ticket.TicketId);
        Assert.False(dbTicket.IsActive);
        Assert.True(dbTicket.IsLostTicket);
        Assert.Equal(_currentTime, dbTicket.CheckOutTime);
    }
    #endregion

    #region Multiple Vehicles
    [Fact]
    public async Task MultipleVehicles_IndependentSessions_CalculatedSeparately()
    {
        _currentTime = new DateTime(2026, 4, 15, 10, 0, 0);
        var t1 = await _manager.CheckInAsync("CAR-1", VehicleType.Car);
        
        _currentTime = new DateTime(2026, 4, 15, 11, 0, 0);
        var t2 = await _manager.CheckInAsync("SUV-2", VehicleType.SUV);
        
        // Checkout SUV-2 at 12:00 -> 1h -> 1500
        _currentTime = new DateTime(2026, 4, 15, 12, 0, 0);
        var result2 = await _manager.CheckOutAsync(t2.TicketId, "000");
        
        // Checkout CAR-1 at 13:00 -> 3h -> 3000
        _currentTime = new DateTime(2026, 4, 15, 13, 0, 0);
        var result1 = await _manager.CheckOutAsync(t1.TicketId, "000");

        Assert.Equal(1500m, result2.TotalFee);
        Assert.Equal(3000m, result1.TotalFee);
    }
    #endregion

    #region Error Recovery
    [Fact]
    public async Task CheckOut_PaymentFails_TicketRemainsActive()
    {
        _paymentStub.SetupSequence(p => p.ProcessPaymentAsync(It.IsAny<string>(), It.IsAny<decimal>()))
            .ReturnsAsync(false) // First fails
            .ReturnsAsync(true); // Second succeeds

        _currentTime = new DateTime(2026, 4, 15, 10, 0, 0);
        var ticket = await _manager.CheckInAsync("TEST-003", VehicleType.Car);

        _currentTime = new DateTime(2026, 4, 15, 12, 0, 0);
        
        // First attempt fails
        await Assert.ThrowsAsync<Exception>(() => _manager.CheckOutAsync(ticket.TicketId, "000"));
        var dbTicket = await _repository.GetTicketByIdAsync(ticket.TicketId);
        Assert.True(dbTicket.IsActive); // Should remain active

        // Second attempt succeeds
        var result = await _manager.CheckOutAsync(ticket.TicketId, "000");
        dbTicket = await _repository.GetTicketByIdAsync(ticket.TicketId);
        Assert.False(dbTicket.IsActive);
        Assert.Equal(2000m, result.TotalFee);
    }
    #endregion

    #region Edge-to-Edge Scenarios
    [Fact]
    public async Task FullFlow_OvernightAndHoliday_CalculatesComplexFee()
    {
        _currentTime = new DateTime(2026, 4, 15, 20, 0, 0); // Wed, 8 PM
        var ticket = await _manager.CheckInAsync("NIGHT-1", VehicleType.Car);

        _currentTime = new DateTime(2026, 4, 15, 23, 0, 0); // Wed, 11 PM
        // 3 hours = 3000 base fee
        // + Overnight (crosses 10 PM) = 2000
        // + Holiday (+50% of 3000 = 1500)
        // Total = 3000 + 1500 + 2000 = 6500

        var result = await _manager.CheckOutAsync(ticket.TicketId, "000", isHoliday: true);

        Assert.Equal(6500m, result.TotalFee);
    }

    [Fact]
    public async Task FullFlow_DailyCapReached_DoesNotAddMoreHourlyFees()
    {
        _currentTime = new DateTime(2026, 4, 16, 8, 0, 0);
        var ticket = await _manager.CheckInAsync("CAP-1", VehicleType.Motorcycle);

        _currentTime = new DateTime(2026, 4, 16, 20, 0, 0); // 12 hours -> base 6000. Cap is 4000.
        
        var result = await _manager.CheckOutAsync(ticket.TicketId, "000");

        Assert.Equal(4000m, result.TotalFee);
    }
    #endregion
}
