using Microsoft.EntityFrameworkCore;

// A temporal query compiled after a plain no-tracking query of the same shape runs as that plain query.
// See README.md.

var connection = Environment.GetEnvironmentVariable("REPRO_CONNECTION")
    ?? "Server=localhost,1433;Database=TemporalCacheRepro;User Id=sa;Password=Repro_Passw0rd!;TrustServerCertificate=True";

// A blog with three versions in its history
await using (var setup = new BlogContext(connection))
{
    await setup.Database.EnsureDeletedAsync();
    await setup.Database.EnsureCreatedAsync();
    var blog = new Blog { Name = "First" };
    setup.Blogs.Add(blog);
    await setup.SaveChangesAsync();
    blog.Name = "Second";
    await setup.SaveChangesAsync();
    blog.Name = "Third";
    await setup.SaveChangesAsync();
}

var failed = false;

// 1. TemporalAll alone: every version, as expected.
await using (var db = new BlogContext(connection))
{
    var all = await db.Blogs.TemporalAll().Select(b => b.Name).ToListAsync();
    failed |= !Report("1. TemporalAll alone", all, 3);
}

// 2. The same query compiled after a plain no-tracking query of the same shape. EF makes temporal queries
//    no-tracking (TemporalAll() becomes TemporalAll().AsNoTracking()), so the two cache keys have the same shape.
//    Expected 3 versions; EF returns only the current row.
await using (var db = new BlogContext(connection))
{
    _ = await db.Blogs.AsNoTracking().Where(b => b.Id == 1).Select(b => b.Name).ToListAsync();
    var all = await db.Blogs.TemporalAll().Where(b => b.Id == 1).Select(b => b.Name).ToListAsync();
    failed |= !Report("2. TemporalAll after a plain AsNoTracking query", all, 3);
}

// 3. A table that isn't temporal: TemporalAll should fail (SQL Server refuses FOR SYSTEM_TIME), and does when alone,
//    but after a plain no-tracking query it silently runs as that query.
await using (var db = new BlogContext(connection))
{
    _ = await db.Posts.AsNoTracking().ToListAsync();
    try
    {
        await db.Posts.TemporalAll().ToListAsync();
        Console.WriteLine("3. TemporalAll of a non-temporal table after a plain AsNoTracking query: NOT refused  <-- BUG");
        failed = true;
    }
    catch (Exception e)
    {
        Console.WriteLine($"3. TemporalAll of a non-temporal table after a plain AsNoTracking query: refused ({e.GetType().Name})");
    }
}

return failed ? 1 : 0;

static bool Report(string label, List<string> rows, int expected)
{
    var ok = rows.Count == expected;
    Console.WriteLine($"{label}: [{string.Join(", ", rows)}] expected {expected} rows{(ok ? "" : "  <-- BUG")}");
    return ok;
}

public class Blog
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class Post
{
    public int Id { get; set; }
}

public class BlogContext(string connection) : DbContext
{
    public DbSet<Blog> Blogs => Set<Blog>();
    public DbSet<Post> Posts => Set<Post>();

    // Every context shares EF's internal service provider, and so its compiled-query cache (EF's default)
    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseSqlServer(connection);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Blog>().ToTable("Blogs", t => t.IsTemporal());
}
