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
    // Test the free parking window and its boundaries
    #endregion

    #region Duration Rounding
    // Test how partial hours are rounded for billing
    #endregion

    #region Daily Cap
    // Test that fees respect maximum daily limits per vehicle type
    #endregion

    #region Overnight Fee
    // Test the flat fee applied for sessions that extend into late hours
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
