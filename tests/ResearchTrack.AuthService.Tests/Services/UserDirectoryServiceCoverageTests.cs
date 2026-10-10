using Microsoft.EntityFrameworkCore;
using ResearchTrack.AuthService.Domain;
using ResearchTrack.AuthService.Features.Users;
using ResearchTrack.AuthService.Persistence;

namespace ResearchTrack.AuthService.Tests.Services;

public sealed class UserDirectoryServiceCoverageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ab")]
    public async Task SearchStudentsAsync_ShortOrBlankQuery_ReturnsEmptyWithoutDatabase(string? query)
    {
        var sut = new UserDirectoryService(new ThrowingFactory());

        var result = await sut.SearchStudentsAsync(query, TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task SearchStudentsAsync_MatchesStudentFields_ExcludesSupervisors_AndOrdersByName()
    {
        var factory = new TestAuthDbContextFactory();
        await SeedAsync(factory,
            Student("sam.one@example.edu", "Sam", "One", "IT1001"),
            Student("alex.sam@example.edu", "Alex", "Beta", "IT2002"),
            Supervisor("sam.supervisor@example.edu", "Sam", "Supervisor"));
        var sut = new UserDirectoryService(factory);

        var result = await sut.SearchStudentsAsync(" sam ", TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alex", result[0].FirstName);
        Assert.Equal("Sam", result[1].FirstName);
        Assert.All(result, item => Assert.Equal("STUDENT", item.Role));
    }

    [Fact]
    public async Task SearchStudentsAsync_RegistrationNumberMatch_ReturnsStudent()
    {
        var factory = new TestAuthDbContextFactory();
        var student = Student("student@example.edu", "Nina", "Perera", "IT998877");
        await SeedAsync(factory, student);
        var sut = new UserDirectoryService(factory);

        var result = await sut.SearchStudentsAsync("998877", TestContext.Current.CancellationToken);

        var item = Assert.Single(result);
        Assert.Equal(student.Id, item.Id);
        Assert.Equal("IT998877", item.RegistrationNumber);
    }

    [Fact]
    public async Task ResolveStudentsAsync_EmptyInput_ReturnsEmptyWithoutDatabase()
    {
        var sut = new UserDirectoryService(new ThrowingFactory());

        var result = await sut.ResolveStudentsAsync([], TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ResolveStudentsAsync_DeduplicatesIds_AndExcludesSupervisor()
    {
        var factory = new TestAuthDbContextFactory();
        var student = Student("student@example.edu", "Asha", "Silva", "IT123");
        var supervisor = Supervisor("supervisor@example.edu", "Zed", "Supervisor");
        await SeedAsync(factory, student, supervisor);
        var sut = new UserDirectoryService(factory);

        var result = await sut.ResolveStudentsAsync(
            [student.Id, student.Id, supervisor.Id],
            TestContext.Current.CancellationToken);

        var item = Assert.Single(result);
        Assert.Equal(student.Id, item.Id);
        Assert.Equal("STUDENT", item.Role);
    }

    [Fact]
    public async Task GetUserAsync_ReturnsMappedUser_AndNullForMissingUser()
    {
        var factory = new TestAuthDbContextFactory();
        var supervisor = Supervisor("supervisor@example.edu", "Ada", "Lovelace");
        await SeedAsync(factory, supervisor);
        var sut = new UserDirectoryService(factory);

        var found = await sut.GetUserAsync(supervisor.Id, TestContext.Current.CancellationToken);
        var missing = await sut.GetUserAsync(Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal("SUPERVISOR", found!.Role);
        Assert.Equal("Ada", found.FirstName);
        Assert.Null(missing);
    }

    private static User Student(string email, string first, string last, string registration) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        FirstName = first,
        LastName = last,
        PasswordHash = "hash",
        Role = UserRole.Student,
        RegistrationNumber = registration,
        CreatedAt = DateTime.UtcNow
    };

    private static User Supervisor(string email, string first, string last) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        FirstName = first,
        LastName = last,
        PasswordHash = "hash",
        Role = UserRole.Supervisor,
        CreatedAt = DateTime.UtcNow
    };

    private static async Task SeedAsync(TestAuthDbContextFactory factory, params User[] users)
    {
        await using var db = factory.CreateDbContext();
        db.Users.AddRange(users);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private sealed class TestAuthDbContextFactory : IDbContextFactory<AuthDbContext>
    {
        private readonly DbContextOptions<AuthDbContext> _options =
            new DbContextOptionsBuilder<AuthDbContext>()
                .UseInMemoryDatabase($"auth-directory-{Guid.NewGuid():N}")
                .Options;

        public AuthDbContext CreateDbContext() => new(_options);

        public Task<AuthDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }

    private sealed class ThrowingFactory : IDbContextFactory<AuthDbContext>
    {
        public AuthDbContext CreateDbContext() => throw new InvalidOperationException("Database should not be accessed.");
        public Task<AuthDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Database should not be accessed.");
    }
}
