using SmartPark.Core.Models;
using SmartPark.Core.Services;
using FsCheck;
using FsCheck.Xunit;

namespace SmartPark.Tests;

public class ParkingFeeCalculatorTests
{
    private readonly ParkingFeeCalculator _calculator = new();

    // ────────────────────────────────────────────────────────────
    //  EXAMPLE TEST — shows the naming convention and AAA pattern.
    //  Delete or keep this; it does not count toward your grade.
    // ────────────────────────────────────────────────────────────

    [Fact]
    public void CalculateFee_ZeroDuration_ReturnsFree()
    {
        // Arrange
        var checkIn = new DateTime(2026, 3, 16, 10, 0, 0);  // Monday
        var checkOut = checkIn; // same time = 0 duration

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(0m, result.TotalFee);
    }

    #region Basic Fee Calculation
    [Theory]
    [InlineData(VehicleType.Motorcycle, 2, 1000)]
    [InlineData(VehicleType.Car, 3, 3000)]
    [InlineData(VehicleType.SUV, 1, 1500)]
    public void CalculateFee_BasicHourlyRate_ReturnsCorrectFee(VehicleType vehicleType, int hours, decimal expectedFee)
    {
        // Arrange
        // Use a Monday to avoid weekend surcharge, and 10 AM to avoid overnight fee.
        var checkIn = new DateTime(2026, 4, 20, 10, 0, 0); 
        var checkOut = checkIn.AddHours(hours);

        // Act
        var result = _calculator.CalculateFee(vehicleType, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expectedFee, result.TotalFee);
    }
    #endregion

    #region Grace Period
    [Theory]
    [InlineData(VehicleType.Motorcycle, 29)]
    [InlineData(VehicleType.Car, 30)]
    public void CalculateFee_GracePeriod_ReturnsFree(VehicleType vehicleType, int minutes)
    {
        // Arrange
        var checkIn = new DateTime(2026, 4, 20, 10, 0, 0);
        var checkOut = checkIn.AddMinutes(minutes);

        // Act
        var result = _calculator.CalculateFee(vehicleType, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(0m, result.TotalFee);
    }
    #endregion

    #region Duration Rounding
    [Theory]
    [InlineData(90, 1000)] // exactly 1 hr past grace
    [InlineData(91, 2000)] // 1 hr 1 min past grace -> 2 hrs
    public void CalculateFee_DurationRounding_AlwaysRoundsUp(int totalMinutes, decimal expectedFee)
    {
        // Arrange
        var checkIn = new DateTime(2026, 4, 20, 10, 0, 0);
        var checkOut = checkIn.AddMinutes(totalMinutes);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expectedFee, result.TotalFee);
    }
    #endregion

    #region Daily Cap
    [Theory]
    [InlineData(VehicleType.Motorcycle, 10, 4000)] // 10 * 500 = 5000 -> capped at 4000
    [InlineData(VehicleType.Car, 12, 8000)]        // 12 * 1000 = 12000 -> capped at 8000
    [InlineData(VehicleType.SUV, 10, 12000)]       // 10 * 1500 = 15000 -> capped at 12000
    public void CalculateFee_DailyCap_LimitsMaximumFee(VehicleType vehicleType, int hours, decimal expectedFee)
    {
        // Arrange
        // Start at 8 AM to avoid overnight fees for long durations
        var checkIn = new DateTime(2026, 4, 20, 8, 0, 0);
        var checkOut = checkIn.AddHours(hours);

        // Act
        var result = _calculator.CalculateFee(vehicleType, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expectedFee, result.TotalFee);
    }
    #endregion

    #region Overnight Fee
    [Theory]
    [InlineData("2026-04-20T20:00:00", "2026-04-20T23:00:00", 5000)] // Car 3 hours (3000) + 2000 overnight = 5000
    [InlineData("2026-04-20T23:00:00", "2026-04-21T06:00:00", 9000)] // Car 7 hours (7000) + 2000 overnight = 9000
    [InlineData("2026-04-20T08:00:00", "2026-04-20T17:00:00", 8000)] // Car 9 hours (capped to 8000) no overnight = 8000
    public void CalculateFee_OvernightSession_AddsFlatFee(string checkInStr, string checkOutStr, decimal expectedFee)
    {
        // Arrange
        var checkIn = DateTime.Parse(checkInStr);
        var checkOut = DateTime.Parse(checkOutStr);

        // Act
        var result = _calculator.CalculateFee(VehicleType.Car, MembershipTier.Guest, checkIn, checkOut);

        // Assert
        Assert.Equal(expectedFee, result.TotalFee);
    }
    #endregion

    #region Weekend Surcharge
    // Test the percentage-based surcharge on specific days
    #endregion

    #region Holiday Surcharge
    // Test holiday pricing and its interaction with weekend pricing
    #endregion

    #region Membership Discounts
    // Test discount tiers and what amounts they apply to
    #endregion

    #region Lost Ticket
    // Test the penalty and how it interacts with other fee modifiers
    #endregion

    #region Edge Cases
    // Test invalid inputs and boundary conditions
    #endregion

    #region Property-Based Tests
    // Write at least 5 FsCheck properties that must hold for ALL valid inputs
    // You may need custom Arbitrary<T> for generating valid DateTime pairs
    #endregion
}
