# EF Core: a temporal query runs as a cached plain no-tracking query

A minimal reproduction for EF Core 10.0.14 with SQL Server 2022.

## The bug

`TemporalAll()`, `TemporalAsOf()` and the other temporal operations make a query no-tracking: `Blogs.TemporalAll()` becomes `Blogs.TemporalAll().AsNoTracking()`. Suppose a plain `Blogs.AsNoTracking()` query of the same shape has already been compiled. The temporal query then finds that plain query in the compiled-query cache and runs it.

The result is the current rows instead of the history, with no error. On a table that isn't temporal, the query isn't refused either.

## Why

The compiled-query cache compares keys with `ExpressionEqualityComparer`, and for query roots it calls `left.Equals(right)`. The two roots compare differently:

- `EntityQueryRootExpression.Equals` returns true for any `EntityQueryRootExpression` of the same entity type, including the temporal roots, which derive from it.
- `TemporalAllQueryRootExpression.Equals` (and the other temporal roots) returns true only for its own type.
- All of them have the same hash code.

The cache asks the stored key whether it equals the one being looked up. So a stored plain key says yes to the temporal query.

A temporal query that runs before the plain one, or a tracked plain query, isn't affected: the shapes differ.

## Run it

```sh
docker compose up -d     # SQL Server 2022 on localhost:1433
dotnet run               # exits 1 while the bug is present
```

To use another server, set `REPRO_CONNECTION`.

The GitHub Actions workflow runs the same thing against a SQL Server service container.

## Output

```
1. TemporalAll alone: [Third, First, Second] expected 3 rows
2. TemporalAll after a plain AsNoTracking query: [Third] expected 3 rows  <-- BUG
3. TemporalAll of a non-temporal table after a plain AsNoTracking query: NOT refused  <-- BUG
```
