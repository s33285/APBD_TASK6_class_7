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

            if (!await reader.ReadAsync()) return NotFound(new ErrorResponseDto { Message = $"Appointment {idAppointment} not found." });

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


        [HttpPost]
        public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto dto) 
        { 
            if(string.IsNullOrWhiteSpace(dto.Reason)) return BadRequest(new ErrorResponseDto {Message = "Reason cannot be empty."});
            if(dto.Reason.Length > 250) return BadRequest(new ErrorResponseDto { Message = "Reason must be at most 250 characters." });
            if(dto.AppointmentDate <= DateTime.UtcNow) return BadRequest(new ErrorResponseDto {Message = "Appointment date cannot be in the past."});
        
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            await using var patientCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Patients WHERE IdPatient = @IdPatient AND IsActive = 1;", connection);
            patientCmd.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
            if ((int)(await patientCmd.ExecuteScalarAsync())! == 0) return BadRequest(new ErrorResponseDto {Message = "Patient does not exist or is not active."});

            await using var doctorCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.Doctors WHERE IdDoctor = @IdDoctor AND IsActive = 1;",connection);
            doctorCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
            if ((int)(await doctorCmd.ExecuteScalarAsync())! == 0) return BadRequest(new ErrorResponseDto {Message = "Doctor does not exist or is not active."});

            await using var conflictCmd = new SqlCommand("""
                SELECT COUNT(1)
                FROM dbo.Appointments
                WHERE IdDoctor = @IdDoctor
                  AND AppointmentDate = @AppointmentDate
                  AND Status = N'Scheduled'; 
                """, connection);

            conflictCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
            conflictCmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
            if ((int)(await conflictCmd.ExecuteScalarAsync())! > 0) return Conflict(new ErrorResponseDto {Message = "Doctor already has an appointment at that time."});


            await using var insertCmd = new SqlCommand("""
                INSERT INTO dbo.Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason)
                OUTPUT INSERTED.IdAppointment
                VALUES (@IdPatient, @IdDoctor, @AppointmentDate, N'Scheduled', @Reason);
            """, connection);

            insertCmd.Parameters.Add("@IdPatient", SqlDbType.Int).Value = dto.IdPatient;
            insertCmd.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = dto.IdDoctor;
            insertCmd.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = dto.AppointmentDate;
            insertCmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = dto.Reason;

            var newId = (int)(await insertCmd.ExecuteScalarAsync())!;
            return CreatedAtAction(nameof(GetAppointment), new {idAppointment = newId}, new {IdAppointment = newId});

        }
    }
}
