if (args.Length == 0 || args[0] is "--help" or "-h")
{
    Console.WriteLine("ResearchTrack development seeder");
    Console.WriteLine("Usage: dotnet run --project tools/ResearchTrack.DevSeeder -- <service>");
    Console.WriteLine();
    Console.WriteLine("The seeder host is intentionally infrastructure-only at bootstrap time.");
    Console.WriteLine("Sprint feature developers register development-safe seeders that reuse real domain rules.");
    return 0;
}

Console.Error.WriteLine($"No development seeder is registered for service '{args[0]}'.");
Console.Error.WriteLine("Implement the service seeder alongside the relevant Sprint feature; do not duplicate business rules here.");
return 2;
