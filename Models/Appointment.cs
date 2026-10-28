using System.ComponentModel.DataAnnotations;

namespace LakesideMedical.Models;

public class Appointment
{
    public int Id { get; set; }

    [MaxLength(50)]
    public string Specialty { get; set; } = "";

    [MaxLength(100)]
    public string Doctor { get; set; } = "";

    public DateTime AppointmentDateTime { get; set; }

    [MaxLength(100)]
    public string PatientName { get; set; } = "";

    [MaxLength(30)]
    public string PatientPhone { get; set; } = "";

    [MaxLength(254)]
    public string PatientEmail { get; set; } = "";

    [MaxLength(20)]
    public string Status { get; set; } = "Booked";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(255)]
    public string? StripeSessionId { get; set; }
}
