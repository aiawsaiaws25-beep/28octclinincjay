using System.ComponentModel.DataAnnotations;

namespace LakesideMedical.Models;

public class BookingInput
{
    [Required, StringLength(50)]
    public string Specialty { get; set; } = "";

    [Required, StringLength(100)]
    public string Doctor { get; set; } = "";

    [Required, DataType(DataType.Date), Display(Name = "Appointment date")]
    public DateTime? Date { get; set; }

    [Required, Display(Name = "Time slot")]
    public string Time { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 2), Display(Name = "Patient name")]
    public string PatientName { get; set; } = "";

    [Required, StringLength(30), Phone, Display(Name = "Phone number")]
    [RegularExpression(@"^\+?[0-9][0-9 ()\-]{5,28}[0-9]$", ErrorMessage = "Enter a valid phone number with 7 to 30 characters.")]
    public string PatientPhone { get; set; } = "";

    [Required, StringLength(254), EmailAddress, Display(Name = "Email address")]
    public string PatientEmail { get; set; } = "";
}
