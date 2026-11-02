var jobRequested = args.Length > 0 || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("M365_JOB_ID"));
if (jobRequested)
{
	Console.Error.WriteLine("Assessment work is blocked; no external service adapter is configured.");
	return 2;
}

Console.WriteLine("Assessment worker is disabled and idle.");
return 0;
