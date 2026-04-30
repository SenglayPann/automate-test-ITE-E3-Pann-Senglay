namespace SmartPark.Core.Models;

/// <summary>
/// Breakdown of a calculated parking fee.
/// </summary>
public class ParkingFeeResult
{
    public decimal BaseFee { get; set; }
    public decimal SurchargeAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LostTicketPenalty { get; set; }
    public decimal OvernightFee { get; set; }
    public decimal TotalFee { get; set; }
    public string Breakdown { get; set; } = string.Empty;
}
