using Moq;
using SmartPark.Core.Interfaces;
using SmartPark.Core.Models;
using SmartPark.Core.Services;

namespace SmartPark.Tests;

public class ParkingSessionManagerTests
{
    // ────────────────────────────────────────────────────────────
    //  SHARED SETUP — create test doubles and the system-under-test.
    //  Moq's Mock<T> creates test doubles that can act as:
    //    - Stubs: .Setup().Returns() — provide canned answers
    //    - Mocks: .Verify()         — assert interactions happened
    //  You can use a constructor, or duplicate this in each test.
    // ────────────────────────────────────────────────────────────

    private readonly Mock<IPaymentGateway> _paymentStub = new();
    private readonly Mock<INotificationService> _notificationStub = new();
    private readonly Mock<IMembershipService> _membershipStub = new();
    private readonly Mock<IParkingRepository> _repoStub = new();
    private readonly Mock<IDateTimeProvider> _dateTimeStub = new();
    private readonly ParkingFeeCalculator _feeCalculator = new();
    private readonly ParkingSessionManager _manager;

    public ParkingSessionManagerTests()
    {
        _manager = new ParkingSessionManager(
            _feeCalculator,
            _paymentStub.Object,
            _notificationStub.Object,
            _membershipStub.Object,
            _repoStub.Object,
            _dateTimeStub.Object);
    }

    // ────────────────────────────────────────────────────────────
    //  EXAMPLE TEST — shows stub setup + mock verification pattern.
    //  .Setup().Returns() = STUB behavior (canned answer)
    //  .Verify()          = MOCK behavior (interaction assertion)
    //  Delete or keep this; it does not count toward your grade.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckInAsync_NewVehicle_LookUpMembership()
    {
        // Arrange — configure stubs (canned return values)
        _membershipStub.Setup(m => m.GetMembershipTier("PP-9999")).Returns(MembershipTier.Guest);
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("PP-9999")).ReturnsAsync((ParkingTicket?)null);
        _dateTimeStub.Setup(d => d.Now).Returns(new DateTime(2026, 3, 16, 10, 0, 0));

        // Act
        var ticket = await _manager.CheckInAsync("PP-9999", VehicleType.Car);

        // Assert — verify as mock (was this interaction called?)
        _membershipStub.Verify(m => m.GetMembershipTier("PP-9999"), Times.Once);
        Assert.Equal("PP-9999", ticket.Vehicle.LicensePlate);
    }

    #region CheckIn — Happy Path
    [Fact]
    public async Task CheckInAsync_ValidInput_SavesAndReturnsTicket()
    {
        // Arrange
        _membershipStub.Setup(m => m.GetMembershipTier("AA-1111")).Returns(MembershipTier.Gold);
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("AA-1111")).ReturnsAsync((ParkingTicket?)null);
        var now = new DateTime(2026, 4, 25, 10, 0, 0);
        _dateTimeStub.Setup(d => d.Now).Returns(now);

        // Act
        var ticket = await _manager.CheckInAsync("AA-1111", VehicleType.SUV);

        // Assert
        Assert.Equal("AA-1111", ticket.Vehicle.LicensePlate);
        Assert.Equal(VehicleType.SUV, ticket.Vehicle.Type);
        Assert.Equal(MembershipTier.Gold, ticket.Vehicle.Membership);
        Assert.Equal(now, ticket.CheckInTime);
        _repoStub.Verify(r => r.SaveTicketAsync(ticket), Times.Once);
    }
    #endregion

    #region CheckIn — Validation
    [Fact]
    public async Task CheckInAsync_VehicleAlreadyCheckedIn_ThrowsInvalidOperationException()
    {
        // Arrange
        var existingTicket = new ParkingTicket();
        _repoStub.Setup(r => r.GetActiveTicketByPlateAsync("AA-1111")).ReturnsAsync(existingTicket);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.CheckInAsync("AA-1111", VehicleType.Car));
        _repoStub.Verify(r => r.SaveTicketAsync(It.IsAny<ParkingTicket>()), Times.Never);
    }
    #endregion

    #region CheckOut — Happy Path
    [Fact]
    public async Task CheckOutAsync_ValidTicket_ProcessesPaymentAndUpdatesTicket()
    {
        // Arrange
        var ticket = new ParkingTicket
        {
            TicketId = "T-123",
            Vehicle = new Vehicle { LicensePlate = "BB-2222", Type = VehicleType.Car, Membership = MembershipTier.Guest },
            CheckInTime = new DateTime(2026, 4, 20, 10, 0, 0) // Monday
        };
        _repoStub.Setup(r => r.GetTicketByIdAsync("T-123")).ReturnsAsync(ticket);
        _dateTimeStub.Setup(d => d.Now).Returns(new DateTime(2026, 4, 20, 12, 0, 0)); // 2 hours -> 2000 KHR
        _paymentStub.Setup(p => p.ProcessPaymentAsync("T-123", It.IsAny<decimal>())).ReturnsAsync(true);

        // Act
        var feeResult = await _manager.CheckOutAsync("T-123", "0123456789");

        // Assert
        Assert.Equal(2000m, feeResult.TotalFee);
        _paymentStub.Verify(p => p.ProcessPaymentAsync("T-123", 2000m), Times.Once);
        _repoStub.Verify(r => r.UpdateTicketAsync(It.Is<ParkingTicket>(t => t.CheckOutTime != null && !t.IsActive)), Times.Once);
        _notificationStub.Verify(n => n.SendReceiptAsync("0123456789", It.IsAny<string>()), Times.Once);
    }
    #endregion

    #region CheckOut — Payment Failure
    [Fact]
    public async Task CheckOutAsync_PaymentFails_ThrowsExceptionAndDoesNotUpdateTicket()
    {
        // Arrange
        var ticket = new ParkingTicket
        {
            TicketId = "T-123",
            Vehicle = new Vehicle { Type = VehicleType.Car }
        };
        _repoStub.Setup(r => r.GetTicketByIdAsync("T-123")).ReturnsAsync(ticket);
        _paymentStub.Setup(p => p.ProcessPaymentAsync("T-123", It.IsAny<decimal>())).ReturnsAsync(false);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => _manager.CheckOutAsync("T-123", "0123456789"));
        _repoStub.Verify(r => r.UpdateTicketAsync(It.IsAny<ParkingTicket>()), Times.Never);
    }
    #endregion

    #region CheckOut — Notification Failure
    [Fact]
    public async Task CheckOutAsync_NotificationFails_DoesNotFailCheckOut()
    {
        // Arrange
        var ticket = new ParkingTicket { TicketId = "T-123", Vehicle = new Vehicle { Type = VehicleType.Car } };
        _repoStub.Setup(r => r.GetTicketByIdAsync("T-123")).ReturnsAsync(ticket);
        _paymentStub.Setup(p => p.ProcessPaymentAsync("T-123", It.IsAny<decimal>())).ReturnsAsync(true);
        _notificationStub.Setup(n => n.SendReceiptAsync(It.IsAny<string>(), It.IsAny<string>())).ThrowsAsync(new Exception("SMS gateway down"));

        // Act
        var result = await _manager.CheckOutAsync("T-123", "0123456789");

        // Assert
        Assert.NotNull(result);
        _repoStub.Verify(r => r.UpdateTicketAsync(ticket), Times.Once);
    }
    #endregion

    #region CheckOut — Validation
    [Fact]
    public async Task CheckOutAsync_TicketNotFound_ThrowsKeyNotFoundException()
    {
        // Arrange
        _repoStub.Setup(r => r.GetTicketByIdAsync("INVALID")).ReturnsAsync((ParkingTicket)null!);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _manager.CheckOutAsync("INVALID", "000"));
    }

    [Fact]
    public async Task CheckOutAsync_TicketAlreadyProcessed_ThrowsInvalidOperationException()
    {
        // Arrange
        var inactiveTicket = new ParkingTicket { TicketId = "TEST", Vehicle = new Vehicle { Type = VehicleType.Car }, CheckOutTime = DateTime.Now };
        _repoStub.Setup(r => r.GetTicketByIdAsync("TEST")).ReturnsAsync(inactiveTicket);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _manager.CheckOutAsync("TEST", "000"));
    }
    #endregion
}
