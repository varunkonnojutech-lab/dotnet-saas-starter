using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SaasStarter.Api.Dtos;
using SaasStarter.Infrastructure.Data;

namespace SaasStarter.Api.Controllers;

/// <summary>Public plan catalog — powers the pricing section on the landing page.</summary>
[ApiController]
[Route("api/plans")]
public class PlansController : ControllerBase
{
    private readonly AppDbContext _db;

    public PlansController(AppDbContext db) => _db = db;

    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PlanDto>>> GetPlans()
    {
        var plans = await _db.Plans
            .OrderBy(p => p.MonthlyPriceCents)
            .ToListAsync();
        return Ok(plans.Select(PlanDto.From).ToList());
    }
}
