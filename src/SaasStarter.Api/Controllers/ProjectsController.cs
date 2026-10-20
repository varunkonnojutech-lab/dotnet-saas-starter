using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasStarter.Api.Dtos;
using SaasStarter.Domain;
using SaasStarter.Infrastructure.Data;
using SaasStarter.Infrastructure.Tenancy;

namespace SaasStarter.Api.Controllers;

/// <summary>
/// Sample tenant-owned resource. Every query below relies on the EF Core
/// global query filter in AppDbContext — there is deliberately no
/// "WHERE TenantId = ..." anywhere in this controller, and the integration
/// tests prove one tenant still can't see another tenant's projects.
/// </summary>
[ApiController]
[Route("api/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITenantContext _tenant;

    public ProjectsController(AppDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> GetAll()
    {
        var projects = await _db.Projects
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync();
        return Ok(projects.Select(ProjectDto.From).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDto>> GetById(Guid id)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id);
        return project is null ? NotFound() : Ok(ProjectDto.From(project));
    }

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest request)
    {
        // Plan quota enforcement (MaxProjects of -1 = unlimited). The count
        // query is tenant-filtered automatically, like everything else here.
        var tenant = await _db.Tenants.Include(t => t.Plan)
            .FirstAsync(t => t.Id == _tenant.TenantId);
        if (tenant.Plan.MaxProjects >= 0)
        {
            var count = await _db.Projects.CountAsync();
            if (count >= tenant.Plan.MaxProjects)
            {
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Plan limit reached",
                    detail: $"The {tenant.Plan.Name} plan allows {tenant.Plan.MaxProjects} projects. Upgrade to create more.");
            }
        }

        var project = new Project
        {
            // TenantId intentionally not set: AppDbContext stamps it from
            // ITenantContext on save, so it can never be spoofed by a client.
            Name = request.Name,
            Description = request.Description,
            CreatedByUserId = User.GetUserId()
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = project.Id }, ProjectDto.From(project));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProjectDto>> Update(Guid id, UpdateProjectRequest request)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id);
        if (project is null)
        {
            return NotFound();
        }

        project.Name = request.Name;
        project.Description = request.Description;
        await _db.SaveChangesAsync();
        return Ok(ProjectDto.From(project));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == id);
        if (project is null)
        {
            return NotFound();
        }

        _db.Projects.Remove(project);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
