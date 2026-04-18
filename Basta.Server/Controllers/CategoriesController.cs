using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Basta.Server.Data;
using Basta.Server.DTOs;

namespace Basta.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly BastaDbContext _context;

    public CategoriesController(BastaDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetCategories([FromQuery] string lang = "en")
    {
        var isSpanish = lang.Equals("es", StringComparison.OrdinalIgnoreCase);

        var categories = await _context.Categories
            .Select(c => new CategoryDto(
                c.CategoryId,
                isSpanish ? c.SpanishName : c.EnglishName
            ))
            .ToListAsync();

        return Ok(categories);
    }
}
