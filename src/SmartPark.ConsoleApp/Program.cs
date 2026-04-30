using Microsoft.EntityFrameworkCore;
using SmartPark.Core.Data;
using SmartPark.Core.Interfaces;
using SmartPark.Core.Models;
using SmartPark.Core.Services;

// ──────────────────────────────────────────────
//  SETUP — SQLite database + services
// ──────────────────────────────────────────────

const string DbFile = "smartpark.db";

var dbOptions = new DbContextOptionsBuilder<SmartParkDbContext>()
    .UseSqlite($"Data Source={DbFile}")
    .Options;

using var db = new SmartParkDbContext(dbOptions);
db.Database.EnsureCreated();

// Seed demo data on first run
if (!db.ParkingTickets.Any())
{
    SeedDemoData(db);
    Info("Demo data loaded — see 'View Active Tickets' to explore.");
}

var repository = new EfParkingRepository(db);
var feeCalculator = new ParkingFeeCalculator();
var paymentGateway = new StubPaymentGateway();
var notificationService = new StubNotificationService();
var membershipService = new DemoMembershipService();
var dateTimeProvider = new SystemDateTimeProvider();

var sessionManager = new ParkingSessionManager(
    feeCalculator, paymentGateway, notificationService,
    membershipService, repository, dateTimeProvider);

// ──────────────────────────────────────────────
//  MAIN MENU LOOP
// ──────────────────────────────────────────────

var running = true;
ShowWelcome();

while (running)
{
    ShowMenu();
    var choice = Ask("Select option");

    switch (choice)
    {
        case "1": await HandleCheckIn(sessionManager); break;
        case "2": await HandleCheckOut(sessionManager); break;
        case "3": await HandleViewActive(repository); break;
        case "4": HandleQuickEstimate(feeCalculator); break;
        case "5": await HandleViewHistory(db); break;
        case "6":
            running = false;
            Info("Goodbye! Database saved to " + DbFile);
            break;
        default:
            Warn("Invalid option. Enter a number 1-6.");
            break;
    }
}

// ══════════════════════════════════════════════
//  MENU DISPLAY
// ══════════════════════════════════════════════

static void ShowWelcome()
{
    Console.WriteLine();
    WriteColor("  ╔══════════════════════════════════════╗", ConsoleColor.Cyan);
    WriteColor("  ║       SMARTPARK PARKING SYSTEM       ║", ConsoleColor.Cyan);
    WriteColor("  ║     Parking Fee Calculator v1.0      ║", ConsoleColor.Cyan);
    WriteColor("  ╚══════════════════════════════════════╝", ConsoleColor.Cyan);
    Console.WriteLine();
    WriteColor("  Tip: Some vehicles are pre-loaded for you to test with.", ConsoleColor.DarkGray);
    WriteColor("  Use 'View Active Tickets' to see their ticket IDs.", ConsoleColor.DarkGray);
    Console.WriteLine();
}

static void ShowMenu()
{
    Console.WriteLine();
    WriteColor("  ┌──────────────────────────────────────┐", ConsoleColor.DarkCyan);
    WriteColor("  │  1.  Check In Vehicle                │", ConsoleColor.DarkCyan);
    WriteColor("  │  2.  Check Out Vehicle               │", ConsoleColor.DarkCyan);
    WriteColor("  │  3.  View Active Tickets             │", ConsoleColor.DarkCyan);
    WriteColor("  │  4.  Quick Fee Estimate              │", ConsoleColor.DarkCyan);
    WriteColor("  │  5.  View Checkout History            │", ConsoleColor.DarkCyan);
    WriteColor("  │  6.  Exit                            │", ConsoleColor.DarkCyan);
    WriteColor("  └──────────────────────────────────────┘", ConsoleColor.DarkCyan);
}

// ══════════════════════════════════════════════
//  HANDLERS
// ══════════════════════════════════════════════

static async Task HandleCheckIn(ParkingSessionManager manager)
{
    Header("CHECK IN");

    var plate = Ask("License plate (e.g. PP-1234)").ToUpper();
    if (string.IsNullOrWhiteSpace(plate)) { Warn("Plate cannot be empty."); return; }

    var vehicleType = AskVehicleType();
    if (vehicleType == null) return;

    try
    {
        var ticket = await manager.CheckInAsync(plate, vehicleType.Value);
        Console.WriteLine();
        Success($"Check-in successful!");
        Console.WriteLine($"  Ticket ID  : {ticket.TicketId}");
        Console.WriteLine($"  Vehicle    : {plate} ({vehicleType})");
        Console.WriteLine($"  Membership : {ticket.Vehicle.Membership}");
        Console.WriteLine($"  Time       : {ticket.CheckInTime:yyyy-MM-dd HH:mm:ss}");
        Hint("Save the Ticket ID — you'll need it for check-out.");
    }
    catch (InvalidOperationException ex) { Error(ex.Message); }
}

static async Task HandleCheckOut(ParkingSessionManager manager)
{
    Header("CHECK OUT");

    var ticketId = Ask("Ticket ID (e.g. A1B2C3D4)").ToUpper();
    if (string.IsNullOrWhiteSpace(ticketId)) { Warn("Ticket ID cannot be empty."); return; }

    var phone = Ask("Phone number (e.g. 012-345-678)");

    var isLost = AskYesNo("Is this a lost ticket?");

    try
    {
        var result = await manager.CheckOutAsync(ticketId, phone, isLost);
        Console.WriteLine();
        Success("Check-out complete! Fee breakdown:");
        Console.WriteLine();
        WriteColor("  ┌─── Fee Breakdown ──────────────────────┐", ConsoleColor.Yellow);
        Console.WriteLine($"  │  Base Fee       : {result.BaseFee,10:N0} KHR │");
        if (result.SurchargeAmount > 0)
            Console.WriteLine($"  │  Surcharge      : +{result.SurchargeAmount,9:N0} KHR │");
        if (result.DiscountAmount > 0)
            Console.WriteLine($"  │  Discount       : -{result.DiscountAmount,9:N0} KHR │");
        if (result.OvernightFee > 0)
            Console.WriteLine($"  │  Overnight Fee  : +{result.OvernightFee,9:N0} KHR │");
        if (result.LostTicketPenalty > 0)
            Console.WriteLine($"  │  Lost Ticket    : +{result.LostTicketPenalty,9:N0} KHR │");
        WriteColor($"  │  ─────────────────────────────────────  │", ConsoleColor.Yellow);
        WriteColor($"  │  TOTAL          : {result.TotalFee,10:N0} KHR │", ConsoleColor.Green);
        WriteColor("  └────────────────────────────────────────┘", ConsoleColor.Yellow);
    }
    catch (KeyNotFoundException) { Error("Ticket not found. Check the ID and try again."); }
    catch (InvalidOperationException ex) { Error(ex.Message); }
    catch (Exception ex) { Error(ex.Message); }
}

static async Task HandleViewActive(EfParkingRepository repository)
{
    Header("ACTIVE TICKETS");

    var tickets = (await repository.GetAllActiveTicketsAsync()).ToList();

    if (tickets.Count == 0)
    {
        Info("No vehicles currently parked.");
        return;
    }

    Console.WriteLine($"  {"Ticket ID",-12} {"Plate",-12} {"Type",-12} {"Membership",-12} {"Check-In"}");
    WriteColor($"  {new string('─', 70)}", ConsoleColor.DarkGray);
    foreach (var t in tickets)
    {
        Console.WriteLine(
            $"  {t.TicketId,-12} {t.Vehicle.LicensePlate,-12} {t.Vehicle.Type,-12} " +
            $"{t.Vehicle.Membership,-12} {t.CheckInTime:yyyy-MM-dd HH:mm}");
    }
    Console.WriteLine();
    Info($"{tickets.Count} vehicle(s) currently parked.");
}

static async Task HandleViewHistory(SmartParkDbContext db)
{
    Header("CHECKOUT HISTORY");

    var tickets = await db.ParkingTickets
        .Where(t => t.CheckOutTime != null)
        .OrderByDescending(t => t.CheckOutTime)
        .Take(10)
        .ToListAsync();

    if (tickets.Count == 0)
    {
        Info("No completed sessions yet. Try checking out a vehicle first!");
        return;
    }

    Console.WriteLine($"  {"Ticket",-10} {"Plate",-10} {"Type",-10} {"In",-18} {"Out",-18} {"Lost?"}");
    WriteColor($"  {new string('─', 72)}", ConsoleColor.DarkGray);
    foreach (var t in tickets)
    {
        Console.WriteLine(
            $"  {t.TicketId,-10} {t.Vehicle.LicensePlate,-10} {t.Vehicle.Type,-10} " +
            $"{t.CheckInTime:MM-dd HH:mm,-18} {t.CheckOutTime:MM-dd HH:mm,-18} " +
            $"{(t.IsLostTicket ? "Yes" : "No")}");
    }
}

static void HandleQuickEstimate(ParkingFeeCalculator calculator)
{
    Header("QUICK FEE ESTIMATE");
    Hint("Calculate a fee without processing payment.");
    Console.WriteLine();

    var vehicleType = AskVehicleType();
    if (vehicleType == null) return;

    var checkIn = AskDateTime("Check-in time");
    if (checkIn == null) return;

    var checkOut = AskDateTime("Check-out time");
    if (checkOut == null) return;

    var membership = AskMembership();
    var isHoliday = AskYesNo("Is it a holiday?");

    try
    {
        var result = calculator.CalculateFee(vehicleType.Value, membership, checkIn.Value, checkOut.Value, isHoliday: isHoliday);
        Console.WriteLine();
        Success("Estimate:");
        Console.WriteLine($"  {result.Breakdown}");
    }
    catch (ArgumentException ex) { Error(ex.Message); }
}

// ══════════════════════════════════════════════
//  INPUT HELPERS — friendly prompts with examples
// ══════════════════════════════════════════════

static string Ask(string prompt)
{
    WriteColor($"  {prompt}: ", ConsoleColor.White);
    return Console.ReadLine()?.Trim() ?? "";
}

static VehicleType? AskVehicleType()
{
    Console.WriteLine("  Vehicle type:");
    Console.WriteLine("    [1] Motorcycle");
    Console.WriteLine("    [2] Car");
    Console.WriteLine("    [3] SUV");
    var input = Ask("Select (1-3)");
    return input switch
    {
        "1" => VehicleType.Motorcycle,
        "2" => VehicleType.Car,
        "3" => VehicleType.SUV,
        _ => NullWarn<VehicleType>("Invalid vehicle type. Enter 1, 2, or 3.")
    };
}

static MembershipTier AskMembership()
{
    Console.WriteLine("  Membership tier:");
    Console.WriteLine("    [0] Guest    (no discount)");
    Console.WriteLine("    [1] Silver   (10% off)");
    Console.WriteLine("    [2] Gold     (25% off)");
    Console.WriteLine("    [3] Platinum (40% off)");
    var input = Ask("Select (0-3, default=0)");
    return input switch
    {
        "1" => MembershipTier.Silver,
        "2" => MembershipTier.Gold,
        "3" => MembershipTier.Platinum,
        _ => MembershipTier.Guest
    };
}

static DateTime? AskDateTime(string label)
{
    var input = Ask($"{label} (yyyy-MM-dd HH:mm, e.g. 2026-03-15 08:30)");
    if (DateTime.TryParse(input, out var result))
        return result;
    Warn($"Could not parse '{input}'. Use format: 2026-03-15 08:30");
    return null;
}

static bool AskYesNo(string question)
{
    var input = Ask($"{question} (y/n, default=n)").ToLower();
    return input is "y" or "yes";
}

// ══════════════════════════════════════════════
//  OUTPUT HELPERS — colored, consistent formatting
// ══════════════════════════════════════════════

static void Header(string title)
{
    Console.WriteLine();
    WriteColor($"  ── {title} ──", ConsoleColor.Cyan);
    Console.WriteLine();
}

static void Success(string msg) => WriteColor($"  ✓ {msg}", ConsoleColor.Green);
static void Error(string msg) => WriteColor($"  ✗ {msg}", ConsoleColor.Red);
static void Warn(string msg) => WriteColor($"  ! {msg}", ConsoleColor.Yellow);
static void Info(string msg) => WriteColor($"  ℹ {msg}", ConsoleColor.DarkGray);
static void Hint(string msg) => WriteColor($"  → {msg}", ConsoleColor.DarkYellow);

static T? NullWarn<T>(string msg) where T : struct
{
    Warn(msg);
    return null;
}

static void WriteColor(string text, ConsoleColor color)
{
    var prev = Console.ForegroundColor;
    Console.ForegroundColor = color;
    Console.WriteLine(text);
    Console.ForegroundColor = prev;
}

// ══════════════════════════════════════════════
//  SEED DATA — pre-loaded vehicles for testing
// ══════════════════════════════════════════════

static void SeedDemoData(SmartParkDbContext db)
{
    var now = DateTime.Now;

    var tickets = new List<ParkingTicket>
    {
        // Active tickets — students can try checking these out
        new()
        {
            TicketId = "DEMO0001",
            Vehicle = new Vehicle { LicensePlate = "PP-1234", Type = VehicleType.Car, Membership = MembershipTier.Gold },
            CheckInTime = now.AddHours(-3)
        },
        new()
        {
            TicketId = "DEMO0002",
            Vehicle = new Vehicle { LicensePlate = "SR-5678", Type = VehicleType.Motorcycle, Membership = MembershipTier.Guest },
            CheckInTime = now.AddMinutes(-20)  // within grace period
        },
        new()
        {
            TicketId = "DEMO0003",
            Vehicle = new Vehicle { LicensePlate = "BTB-9999", Type = VehicleType.SUV, Membership = MembershipTier.Platinum },
            CheckInTime = now.AddHours(-8)
        },
        new()
        {
            TicketId = "DEMO0004",
            Vehicle = new Vehicle { LicensePlate = "KT-0042", Type = VehicleType.Car, Membership = MembershipTier.Silver },
            CheckInTime = now.AddHours(-1)
        },

        // Already checked out — visible in history
        new()
        {
            TicketId = "HIST0001",
            Vehicle = new Vehicle { LicensePlate = "PP-0001", Type = VehicleType.Car, Membership = MembershipTier.Guest },
            CheckInTime = now.AddDays(-1).AddHours(-5),
            CheckOutTime = now.AddDays(-1).AddHours(-2)
        },
        new()
        {
            TicketId = "HIST0002",
            Vehicle = new Vehicle { LicensePlate = "SR-0002", Type = VehicleType.Motorcycle, Membership = MembershipTier.Gold },
            CheckInTime = now.AddDays(-2).AddHours(-1),
            CheckOutTime = now.AddDays(-2),
            IsLostTicket = true
        },
    };

    db.ParkingTickets.AddRange(tickets);
    db.SaveChanges();
}

// ══════════════════════════════════════════════
//  STUB SERVICES — for console app only
// ══════════════════════════════════════════════

class StubPaymentGateway : IPaymentGateway
{
    public Task<bool> ProcessPaymentAsync(string ticketId, decimal amount)
    {
        WriteColor($"  [Payment] ✓ Charged {amount:N0} KHR for ticket {ticketId}", ConsoleColor.DarkGreen);
        return Task.FromResult(true);
    }

    public Task<bool> RefundAsync(string ticketId, decimal amount)
    {
        WriteColor($"  [Payment] ↩ Refunded {amount:N0} KHR for ticket {ticketId}", ConsoleColor.DarkYellow);
        return Task.FromResult(true);
    }

    static void WriteColor(string text, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }
}

class StubNotificationService : INotificationService
{
    public Task SendReceiptAsync(string phoneNumber, string receiptContent)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"  [SMS → {phoneNumber}] Receipt sent.");
        Console.ForegroundColor = prev;
        return Task.CompletedTask;
    }
}

/// <summary>
/// Demo membership service with pre-configured members.
/// Students can see how different tiers affect pricing.
/// </summary>
class DemoMembershipService : IMembershipService
{
    private static readonly Dictionary<string, MembershipTier> Members = new()
    {
        ["PP-1234"] = MembershipTier.Gold,
        ["BTB-9999"] = MembershipTier.Platinum,
        ["KT-0042"] = MembershipTier.Silver,
        ["SR-0002"] = MembershipTier.Gold,
    };

    public MembershipTier GetMembershipTier(string licensePlate)
    {
        return Members.GetValueOrDefault(licensePlate.ToUpper(), MembershipTier.Guest);
    }
}

class SystemDateTimeProvider : IDateTimeProvider
{
    public DateTime Now => DateTime.Now;
}
