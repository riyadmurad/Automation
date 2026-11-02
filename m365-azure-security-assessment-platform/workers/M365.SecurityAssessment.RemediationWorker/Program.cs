var jobRequested = args.Length > 0 || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("M365_JOB_ID"));
if (jobRequested)
{
	Console.Error.WriteLine("Remediation work is blocked; writes and external service access are not configured.");
	return 2;
}

Console.WriteLine("Remediation worker is disabled and idle.");
return 0;
