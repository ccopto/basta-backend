using Basta.Server.Controllers;
using Basta.Server.Data;
using Basta.Server.Entities;
using Basta.Server.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Basta.Server.Tests.Controllers;

public class CategoriesControllerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BastaDbContext _context;
    private readonly CategoriesController _sut;

    public CategoriesControllerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<BastaDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new BastaDbContext(options);
        _context.Database.EnsureCreated();

        _sut = new CategoriesController(_context);
    }

    [Fact]
    public async Task GetCategories_Default_ReturnsEnglishNames()
    {
        // Act
        var result = await _sut.GetCategories();

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var categories = okResult.Value.Should().BeAssignableTo<IEnumerable<CategoryDto>>().Subject;

        categories.Should().Contain(c => c.Name == "Name");
        categories.Should().Contain(c => c.Name == "Animal");
    }

    [Fact]
    public async Task GetCategories_Spanish_ReturnsSpanishNames()
    {
        // Act
        var result = await _sut.GetCategories(lang: "es");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var categories = okResult.Value.Should().BeAssignableTo<IEnumerable<CategoryDto>>().Subject;

        categories.Should().Contain(c => c.Name == "Nombre");
        categories.Should().Contain(c => c.Name == "Animal");
        categories.Should().Contain(c => c.Name == "Ciudad/País");
    }

    [Fact]
    public async Task GetCategories_CaseInsensitiveLang_Works()
    {
        // Act
        var result = await _sut.GetCategories(lang: "ES");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var categories = okResult.Value.Should().BeAssignableTo<IEnumerable<CategoryDto>>().Subject;

        categories.Should().Contain(c => c.Name == "Nombre");
    }

    [Fact]
    public async Task GetCategories_UnknownLanguage_FallsBackToEnglish()
    {
        // Act
        var result = await _sut.GetCategories(lang: "fr");

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var categories = okResult.Value.Should().BeAssignableTo<IEnumerable<CategoryDto>>().Subject;

        categories.Should().Contain(c => c.Name == "Name");
        categories.Should().Contain(c => c.Name == "Animal");
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
