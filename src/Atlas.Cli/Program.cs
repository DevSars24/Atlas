using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Spectre.Console;

namespace Atlas.Cli;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCmd = new RootCommand("atlas — Atlas Job Queue CLI");

        // ── login ──────────────────────────────────────────────────────────────
        var loginCmd = new Command("login", "Authenticate with the Atlas API");
        var loginUrl  = new Option<string>("--url", () => "http://localhost:5000", "API base URL");
        var loginKey  = new Option<string?>("--api-key", "Use API key instead of username/password");
        var loginEmail = new Option<string?>("--email", "Email address");
        var loginPass  = new Option<string?>("--password", "Password");
        loginCmd.AddOption(loginUrl);
        loginCmd.AddOption(loginKey);
        loginCmd.AddOption(loginEmail);
        loginCmd.AddOption(loginPass);
        loginCmd.SetHandler(async (url, apiKey, email, password) =>
        {
            var config = ConfigStore.Load();
            config.BaseUrl = url;

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                config.ApiKey = apiKey;
                config.JwtToken = null;
                ConfigStore.Save(config);
                AnsiConsole.MarkupLine("[green]✓[/] Saved API key. Config: [grey]~/.atlas/config.json[/]");
                return;
            }

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Provide --api-key OR --email and --password.");
                return;
            }

            using var http = new HttpClient { BaseAddress = new Uri(url) };
            var res = await http.PostAsJsonAsync("/api/auth/login", new { email, password });
            if (!res.IsSuccessStatusCode)
            {
                AnsiConsole.MarkupLine($"[red]Login failed:[/] {res.StatusCode}");
                return;
            }
            var body = await res.Content.ReadFromJsonAsync<JsonElement>();
            config.JwtToken = body.GetProperty("token").GetString();
            config.ApiKey = null;
            ConfigStore.Save(config);
            AnsiConsole.MarkupLine("[green]✓[/] Logged in. Token saved.");
        }, loginUrl, loginKey, loginEmail, loginPass);
        rootCmd.AddCommand(loginCmd);

        // ── job ────────────────────────────────────────────────────────────────
        var jobCmd = new Command("job", "Manage jobs");

        // job enqueue
        var enqueueCmd = new Command("enqueue", "Enqueue a new job");
        var enqTypeOpt  = new Option<string>("--type", "Job type") { IsRequired = true };
        var enqQueueOpt = new Option<string>("--queue", () => "default", "Queue name");
        var enqPayloadOpt = new Option<string>("--payload", () => "{}", "JSON payload");
        var enqPriorityOpt = new Option<string>("--priority", () => "Normal", "Priority: Low|Normal|High|Critical");
        var enqJsonFlag = new Option<bool>("--json", "Output as JSON");
        enqueueCmd.AddOption(enqTypeOpt);
        enqueueCmd.AddOption(enqQueueOpt);
        enqueueCmd.AddOption(enqPayloadOpt);
        enqueueCmd.AddOption(enqPriorityOpt);
        enqueueCmd.AddOption(enqJsonFlag);
        enqueueCmd.SetHandler(async (type, queue, payload, priority, asJson) =>
        {
            var config = ConfigStore.Load();
            using var http = ApiClient.Create(config);
            var body = new { Queue = queue, JobType = type, Payload = payload, Priority = priority };
            var res = await http.PostAsJsonAsync("/api/jobs", body);
            var content = await res.Content.ReadAsStringAsync();
            if (asJson) { Console.WriteLine(content); return; }
            if (!res.IsSuccessStatusCode) { AnsiConsole.MarkupLine($"[red]Error {res.StatusCode}:[/] {content}"); return; }
            var doc = JsonDocument.Parse(content).RootElement;
            AnsiConsole.MarkupLine($"[green]✓[/] Job enqueued: [yellow]{doc.GetProperty("id").GetString()}[/]");
        }, enqTypeOpt, enqQueueOpt, enqPayloadOpt, enqPriorityOpt, enqJsonFlag);
        jobCmd.AddCommand(enqueueCmd);

        // job list
        var listCmd = new Command("list", "List jobs");
        var listQueueOpt  = new Option<string?>("--queue", "Filter by queue");
        var listStatusOpt = new Option<string?>("--status", "Filter by status");
        var listJsonFlag  = new Option<bool>("--json", "Output as JSON");
        listCmd.AddOption(listQueueOpt);
        listCmd.AddOption(listStatusOpt);
        listCmd.AddOption(listJsonFlag);
        listCmd.SetHandler(async (queue, status, asJson) =>
        {
            var config = ConfigStore.Load();
            using var http = ApiClient.Create(config);
            var qs = new StringBuilder("/api/jobs?page=1&pageSize=50");
            if (queue != null) qs.Append($"&queue={queue}");
            if (status != null) qs.Append($"&status={status}");
            var res = await http.GetAsync(qs.ToString());
            var content = await res.Content.ReadAsStringAsync();
            if (asJson) { Console.WriteLine(content); return; }
            var jobs = JsonDocument.Parse(content).RootElement;
            var table = new Table()
                .AddColumn("ID").AddColumn("Type").AddColumn("Queue")
                .AddColumn("Status").AddColumn("Attempts").AddColumn("Created");
            foreach (var job in jobs.EnumerateArray())
            {
                table.AddRow(
                    job.GetProperty("id").GetString() ?? "",
                    job.GetProperty("jobType").GetString() ?? "",
                    job.GetProperty("queue").GetString() ?? "",
                    job.GetProperty("status").GetString() ?? "",
                    job.GetProperty("attempts").GetInt32().ToString(),
                    job.GetProperty("createdAt").GetString() ?? ""
                );
            }
            AnsiConsole.Write(table);
        }, listQueueOpt, listStatusOpt, listJsonFlag);
        jobCmd.AddCommand(listCmd);

        // job retry
        var retryCmd = new Command("retry", "Retry a failed job");
        var retryIdArg = new Argument<string>("id", "Job ID");
        retryCmd.AddArgument(retryIdArg);
        retryCmd.SetHandler(async (id) =>
        {
            var config = ConfigStore.Load();
            using var http = ApiClient.Create(config);
            var res = await http.PostAsync($"/api/jobs/{id}/retry", null);
            if (res.IsSuccessStatusCode) AnsiConsole.MarkupLine($"[green]✓[/] Job {id} queued for retry.");
            else AnsiConsole.MarkupLine($"[red]Error {res.StatusCode}:[/] {await res.Content.ReadAsStringAsync()}");
        }, retryIdArg);
        jobCmd.AddCommand(retryCmd);

        rootCmd.AddCommand(jobCmd);

        // ── schedule ───────────────────────────────────────────────────────────
        var schedCmd = new Command("schedule", "Manage cron schedules");

        var schedCreateCmd = new Command("create", "Create a cron schedule");
        var scNameOpt   = new Option<string>("--name", "Schedule name") { IsRequired = true };
        var scCronOpt   = new Option<string>("--cron", "Cron expression (e.g. '0 * * * *')") { IsRequired = true };
        var scTypeOpt   = new Option<string>("--type", "Job type") { IsRequired = true };
        var scQueueOpt  = new Option<string>("--queue", () => "default", "Queue");
        var scPayOpt    = new Option<string>("--payload", () => "{}", "JSON payload");
        schedCreateCmd.AddOption(scNameOpt); schedCreateCmd.AddOption(scCronOpt);
        schedCreateCmd.AddOption(scTypeOpt); schedCreateCmd.AddOption(scQueueOpt);
        schedCreateCmd.AddOption(scPayOpt);
        schedCreateCmd.SetHandler(async (name, cron, type, queue, payload) =>
        {
            var config = ConfigStore.Load();
            using var http = ApiClient.Create(config);
            var body = new { Name = name, CronExpression = cron, JobType = type, Queue = queue, Payload = payload };
            var res = await http.PostAsJsonAsync("/api/schedules", body);
            var content = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode) { AnsiConsole.MarkupLine($"[red]Error {res.StatusCode}:[/] {content}"); return; }
            var doc = JsonDocument.Parse(content).RootElement;
            AnsiConsole.MarkupLine($"[green]✓[/] Schedule created: [yellow]{doc.GetProperty("id").GetString()}[/]");
        }, scNameOpt, scCronOpt, scTypeOpt, scQueueOpt, scPayOpt);
        schedCmd.AddCommand(schedCreateCmd);

        var schedListCmd = new Command("list", "List all schedules");
        schedListCmd.SetHandler(async () =>
        {
            var config = ConfigStore.Load();
            using var http = ApiClient.Create(config);
            var res = await http.GetAsync("/api/schedules");
            var content = await res.Content.ReadAsStringAsync();
            var items = JsonDocument.Parse(content).RootElement;
            var table = new Table()
                .AddColumn("ID").AddColumn("Name").AddColumn("Cron")
                .AddColumn("Type").AddColumn("Queue").AddColumn("Enabled").AddColumn("Next Run");
            foreach (var s in items.EnumerateArray())
            {
                table.AddRow(
                    s.GetProperty("id").GetString() ?? "",
                    s.GetProperty("name").GetString() ?? "",
                    s.GetProperty("cronExpression").GetString() ?? "",
                    s.GetProperty("jobType").GetString() ?? "",
                    s.GetProperty("queue").GetString() ?? "",
                    s.GetProperty("isEnabled").GetBoolean() ? "[green]Yes[/]" : "[red]No[/]",
                    s.TryGetProperty("nextRunAt", out var nr) ? nr.GetString() ?? "-" : "-"
                );
            }
            AnsiConsole.Write(table);
        });
        schedCmd.AddCommand(schedListCmd);

        rootCmd.AddCommand(schedCmd);

        // ── worker ─────────────────────────────────────────────────────────────
        var workerCmd = new Command("worker", "Inspect workers");
        var workerListCmd = new Command("list", "List active workers");
        workerListCmd.SetHandler(async () =>
        {
            var config = ConfigStore.Load();
            using var http = ApiClient.Create(config);
            var res = await http.GetAsync("/api/workers");
            var content = await res.Content.ReadAsStringAsync();
            var workers = JsonDocument.Parse(content).RootElement;
            var table = new Table().AddColumn("ID").AddColumn("Status").AddColumn("Active Jobs").AddColumn("Max").AddColumn("Last Heartbeat");
            foreach (var w in workers.EnumerateArray())
            {
                table.AddRow(
                    w.GetProperty("id").GetString() ?? "",
                    w.GetProperty("status").GetString() ?? "",
                    w.GetProperty("activeJobs").GetInt32().ToString(),
                    w.GetProperty("concurrencyLimit").GetInt32().ToString(),
                    w.TryGetProperty("lastHeartbeat", out var hb) ? hb.GetString() ?? "-" : "-"
                );
            }
            AnsiConsole.Write(table);
        });
        workerCmd.AddCommand(workerListCmd);
        rootCmd.AddCommand(workerCmd);

        return await rootCmd.InvokeAsync(args);
    }
}
