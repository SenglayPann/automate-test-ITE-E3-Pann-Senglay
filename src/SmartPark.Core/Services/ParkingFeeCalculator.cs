using SmartPark.Core.Models;

namespace SmartPark.Core.Services;

/// <summary>
/// Core pricing engine. Pure calculation service with no external dependencies.
/// Students: implement this class using TDD (Red-Green-Refactor).
/// </summary>
public class ParkingFeeCalculator
{
    // ── Pricing constants (from spec §4) ────────────────────────

    // Hourly rates (KHR)
    private const decimal MotorcycleRatePerHour = 500m;
    private const decimal CarRatePerHour = 1_000m;
    private const decimal SuvRatePerHour = 1_500m;

    // Daily caps (KHR)
    private const decimal MotorcycleDailyCap = 4_000m;
    private const decimal CarDailyCap = 8_000m;
    private const decimal SuvDailyCap = 12_000m;

    // Time-based rules
    private const int GracePeriodMinutes = 30;
    private const decimal OvernightFlatFee = 2_000m;
    private const int OvernightHourThreshold = 22; // 10 PM

    // Surcharges
    private const decimal WeekendSurchargeRate = 0.20m;
    private const decimal HolidaySurchargeRate = 0.50m;

    // Membership discounts
    private const decimal SilverDiscountRate = 0.10m;
    private const decimal GoldDiscountRate = 0.25m;
    private const decimal PlatinumDiscountRate = 0.40m;

    // Penalties
    private const decimal LostTicketPenalty = 20_000m;

    /// <summary>
    /// Calculates the parking fee following the 9-step flow in the spec.
    /// </summary>
    /// <remarks>
    /// Steps:
    ///   1. Validate: checkOut before checkIn → ArgumentException
    ///   2. Grace period: total ≤ 30 min → free (lost-ticket penalty still applies)
    ///   3. Duration: billableHours = ⌈(totalMinutes − 30) / 60⌉, min 1
    ///   4. Base fee: billableHours × hourlyRate, capped at dailyCap
    ///   5. Overnight: +2,000 KHR if session spans past 22:00
    ///   6. Surcharge: weekend +20% OR holiday +50% on baseFee (not both)
    ///   7. Discount: (baseFee + surcharge) × membershipRate
    ///   8. Lost ticket: +20,000 KHR (not subject to discounts)
    ///   9. Total: baseFee + surcharge − discount + overnight + penalty (min 0)
    /// </remarks>
    public ParkingFeeResult CalculateFee(
        VehicleType vehicleType,
        MembershipTier membership,
        DateTime checkIn,
        DateTime checkOut,
        bool isLostTicket = false,
        bool isHoliday = false)
    {
        if (checkOut < checkIn)
            throw new ArgumentException("Check-out cannot be before check-in.");

        var totalDuration = checkOut - checkIn;

        if (totalDuration.TotalMinutes <= GracePeriodMinutes)
        {
            return new ParkingFeeResult { BaseFee = 0, TotalFee = 0 };
        }

        var billableHours = GetBillableHours(totalDuration);
        decimal baseFee = GetCappedBaseFee(billableHours, vehicleType);

        decimal overnightFee = IsOvernightSession(checkIn, checkOut) ? OvernightFlatFee : 0m;
        
        decimal surchargeAmount = 0m;
        if (isHoliday)
        {
            surchargeAmount = baseFee * HolidaySurchargeRate;
        }
        else if (IsWeekendSession(checkIn, checkOut))
        {
            surchargeAmount = baseFee * WeekendSurchargeRate;
        }

        decimal discountRate = membership switch
        {
            MembershipTier.Silver => SilverDiscountRate,
            MembershipTier.Gold => GoldDiscountRate,
            MembershipTier.Platinum => PlatinumDiscountRate,
            _ => 0m
        };

        decimal discountAmount = (baseFee + surchargeAmount) * discountRate;

        decimal totalFee = baseFee + surchargeAmount - discountAmount + overnightFee;

        return new ParkingFeeResult
        {
            BaseFee = baseFee,
            SurchargeAmount = surchargeAmount,
            DiscountAmount = discountAmount,
            TotalFee = totalFee
        };
    }

    private decimal GetHourlyRate(VehicleType type) => type switch
    {
        VehicleType.Motorcycle => MotorcycleRatePerHour,
        VehicleType.Car => CarRatePerHour,
        VehicleType.SUV => SuvRatePerHour,
        _ => throw new ArgumentException("Invalid vehicle type")
    };

    private decimal GetDailyCap(VehicleType type) => type switch
    {
        VehicleType.Motorcycle => MotorcycleDailyCap,
        VehicleType.Car => CarDailyCap,
        VehicleType.SUV => SuvDailyCap,
        _ => throw new ArgumentException("Invalid vehicle type")
    };

    private decimal GetCappedBaseFee(int billableHours, VehicleType vehicleType)
    {
        decimal hourlyRate = GetHourlyRate(vehicleType);
        decimal baseFee = billableHours * hourlyRate;
        decimal dailyCap = GetDailyCap(vehicleType);
        return baseFee > dailyCap ? dailyCap : baseFee;
    }

    private int GetBillableHours(TimeSpan duration)
    {
        return Math.Max(1, (int)Math.Ceiling((duration.TotalMinutes - GracePeriodMinutes) / 60.0));
    }

    private bool IsOvernightSession(DateTime checkIn, DateTime checkOut)
    {
        var temp = checkIn;
        while (temp <= checkOut)
        {
            if (temp.Hour >= OvernightHourThreshold || temp.Hour < 6)
            {
                return true;
            }
            if (temp == checkOut) break;

            temp = temp.AddMinutes(15);
            if (temp > checkOut) temp = checkOut;
        }
        return false;
    }

    private bool IsWeekendSession(DateTime checkIn, DateTime checkOut)
    {
        var temp = checkIn;
        while (temp <= checkOut)
        {
            if (temp.DayOfWeek == DayOfWeek.Saturday || temp.DayOfWeek == DayOfWeek.Sunday)
            {
                return true;
            }
            if (temp == checkOut) break;

            temp = temp.AddMinutes(15);
            if (temp > checkOut) temp = checkOut;
        }
        return false;
    }
}
