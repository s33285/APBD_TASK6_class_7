using APBD_TASK6.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System.Data;

namespace APBD_TASK6.Controllers
{
    [ApiController]
    [Route("api/appointments")]
    public class AppointmentsController : ControllerBase
    {
        private readonly string _connectionString;

        public AppointmentsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Missing 'DefaultConnection' in appsettings.json.");
        }


        [HttpGet]
        public async Task<IActionResult> GetAppointments(
            [FromQuery] string? status,
            [FromQuery] string? patientLastName)
        {
            var results = new List<AppointmentListDto>();
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand("""
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
            """, connection);

            command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value =
            (object?)status ?? DBNull.Value;
            command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 80).Value =
            (object?)patientLastName ?? DBNull.Value;


            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) 
            {
                results.Add(new AppointmentListDto 
                {
                    IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                    AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                    Status = reader.GetString(reader.GetOrdinal("Status")),
                    Reason = reader.GetString(reader.GetOrdinal("Reason")),
                    PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                    PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
                });
            }
            return Ok(results);

        }

        [HttpGet("{idAppointment:int}")]
        public async Task<IActionResult> GetAppointment(int idAppointment) 
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var command = new SqlCommand("""
                SELECT
                    a.IdAppointment,
                    a.AppointmentDate,
                    a.Status,
                    a.Reason,
                    a.InternalNotes,
                    a.CreatedAt,

                    p.FirstName AS PatientFirstName,
                    p.LastName AS PatientLastName,
                    p.Email  AS PatientEmail,
                    p.PhoneNumber AS PatientPhoneNumber,

                    d.FirstName AS DoctorFirstName,
                    d.LastName AS DoctorLastName,
                    d.LicenseNumber, s.Name AS Specialization
                FROM dbo.Appointments a
                JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
                JOIN dbo.Doctors  d ON d.IdDoctor  = a.IdDoctor
                JOIN dbo.Specializations s ON s.IdSpecialization = d.IdSpecialization
                WHERE a.IdAppointment = @IdAppointment;
                """, connection);
            command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = idAppointment;

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync()) return NotFound(new ErrorResponseDto { Message = $"Appointment {idAppointment} not found."});

            var dto = new AppointmentDetailsDto 
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes")) ? null : reader.GetString(reader.GetOrdinal("InternalNotes")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                PatientFirstName = reader.GetString(reader.GetOrdinal("PatientFirstName")),
                PatientLastName = reader.GetString(reader.GetOrdinal("PatientLastName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
                PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber")),
                DoctorFirstName = reader.GetString(reader.GetOrdinal("DoctorFirstName")),
                DoctorLastName = reader.GetString(reader.GetOrdinal("DoctorLastName")),
                DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("LicenseNumber")),
                Specialization = reader.GetString(reader.GetOrdinal("Specialization"))

            };
            return Ok(dto);
        }

    }
}
