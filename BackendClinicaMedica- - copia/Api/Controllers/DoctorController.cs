using AccesoDatos.Contratos;
using Dominio.DTOs;
using Dominio.Servicios;
using Entidades;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.ComponentModel.DataAnnotations;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DoctorController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<DoctorController> _logger;
        private readonly ITriageService _triageService;

        public DoctorController(
            IUnitOfWork unitOfWork,
            IWebHostEnvironment environment,
            ILogger<DoctorController> logger,
            ITriageService triageService)
        {
            _unitOfWork = unitOfWork;
            _environment = environment;
            _logger = logger;
            _triageService = triageService;
        }

        [HttpGet]
        [ProducesResponseType(typeof(List<Doctor>), StatusCodes.Status200OK)]
        public async Task<ActionResult<List<Doctor>>> ObtenerTodos()
        {
            using var uow = _unitOfWork.Create(_environment.IsDevelopment());
            var medicos = await uow.Repositorios.DoctorRepositorio.ObtenerTodosAsync();
            return Ok(medicos);
        }
        [HttpPost]
        [ProducesResponseType(typeof(Doctor), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<Doctor>> Crear([FromBody] CrearDoctorDto dto)
        {
            using var uow = _unitOfWork.Create(_environment.IsDevelopment());
            try
            {
                // Validar que el CUIL no exista en Persona
                var existeCuil = await uow.Repositorios.PersonaRepositorio.ExisteCuilAsync(dto.Cuil);
                if (existeCuil)
                    return BadRequest(new { mensaje = "Ya existe una persona con ese CUIL." });

                // Validar que la matrícula no exista en Doctor
                var existeMatricula = await uow.Repositorios.DoctorRepositorio.ExisteMatriculaAsync(dto.Matricula);
                if (existeMatricula)
                    return BadRequest(new { mensaje = "Ya existe un médico con esa MATRICULA." });

                // Crear Persona
                var persona = new Persona
                {
                    Cuil = dto.Cuil,
                    Apellido = dto.Apellido,
                    Nombre = dto.Nombre,
                    Email = dto.Email
                };
                await uow.Repositorios.PersonaRepositorio.CrearAsync(persona);

                // Crear Doctor
                var doctor = new Doctor
                {
                    PersonaId = persona.PersonaId,
                    Matricula = dto.Matricula
                };
                await uow.Repositorios.DoctorRepositorio.CrearAsync(doctor);

                await uow.GuardarCambios();

                // Opcional: devolver el doctor con los datos de persona
                doctor.Persona = persona;

                return Created("", doctor);
            }
            catch (Exception ex)
            {
                await uow.CancelarCambios();
                _logger.LogError(ex, "Error al crear médico");
                return StatusCode(500, new { mensaje = "Error interno del servidor" });
            }
        }

        [HttpGet("{id}")]
        [ProducesResponseType(typeof(Doctor), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Doctor>> ObtenerPorId(Guid id)
        {
            using var uow = _unitOfWork.Create(_environment.IsDevelopment());
            var medico = await uow.Repositorios.DoctorRepositorio.ObtenerPorIdAsync(id);
            if (medico == null)
                return NotFound(new { mensaje = "Médico no encontrado" });
            return Ok(medico);
        }

        [HttpGet("PorPaciente")]
        [ProducesResponseType(typeof(List<Ingreso>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]

        public async Task<ActionResult<Ingreso>> ObtenerPorPaciente(Guid pacienteId)
        {

            using var uow = _unitOfWork.Create(_environment.IsDevelopment());

            var ingresos = await uow.Repositorios.IngresoRepositorio.ObtenerPorPacienteAsync(pacienteId);

            if (ingresos == null || !ingresos.Any())

                return NotFound(new { mensaje = "No se encontraron ingresos para el paciente." });

            return Ok(ingresos);

        }

        [HttpGet("reclamar-proximo-ingreso")]
        [ProducesResponseType(typeof(Ingreso), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<Ingreso>> ReclamarProximoIngreso( Guid medicoId)
        {
            var resultado = await _triageService.ReclamarProximoPacienteAsync(medicoId);

            if (!resultado.Exitoso)
            {
                if (resultado.Mensaje.Contains("No hay pacientes"))
                    return NotFound(new { mensaje = resultado.Mensaje });
                return BadRequest(new { mensaje = resultado.Mensaje });
            }

            return Ok(resultado.Datos);
        }
        [HttpGet("buscar-id-por-matricula")]
        [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<Guid>> BuscarIdPorMatricula([FromQuery] string matricula)
        {
            using var uow = _unitOfWork.Create(_environment.IsDevelopment());
            var medico = await uow.Repositorios.DoctorRepositorio.ObtenerPorMatriculaAsync(matricula);

            if (medico == null)
                return NotFound(new { mensaje = "No se encontró un médico con esa matrícula." });

            return Ok(medico.PersonaId);
        }

        [HttpPost("registrar-atencion")]
        [ProducesResponseType(typeof(Atencion), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<ActionResult> RegistrarAtencion([FromBody] RegistrarAtencionDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Informe))
                return BadRequest(new { mensaje = "El informe es obligatorio." });

            using var uow = _unitOfWork.Create(_environment.IsDevelopment());

            // Validar que el ingreso existe y está reclamado por el médico
            var ingreso = await uow.Repositorios.IngresoRepositorio.ObtenerPorIdAsync(dto.IngresoId);
            if (ingreso == null)
                return BadRequest(new { mensaje = "El ingreso no existe." });

            //if (ingreso.MedicoId != dto.MedicoId)
            //    return BadRequest(new { mensaje = "El ingreso no fue reclamado por este médico." });

            // Validar que no exista ya una atención para ese ingreso
            var atencionExistente = await uow.Repositorios.AtencionRepositorio.ObtenerPorIngresoAsync(dto.IngresoId);
            if (atencionExistente != null)
                return BadRequest(new { mensaje = "Ya existe una atención registrada para este ingreso." });

            // Registrar la atención
            var atencion = new Atencion
            {
                IngresoId = dto.IngresoId,
                MedicoId = dto.MedicoId,
                Informe = dto.Informe,
                FechaAtencion = DateTime.UtcNow
            };

            await uow.Repositorios.AtencionRepositorio.CrearAsync(atencion);

            // Cambiar estado del ingreso a FINALIZADO
            ingreso.Estado = "FINALIZADO";
            await uow.Repositorios.IngresoRepositorio.ActualizarAsync(ingreso);

            await uow.GuardarCambios();

            return Created("", atencion);
        }
    }
    public record CrearDoctorDto(
        [Required] string Nombre,
        [Required] string Apellido,
        [Required] string Cuil,
        [Required] string Matricula,
        string? Email
    );
}