using ModularPlatform.Jobs;
using Quartz;
using Quartz.Impl.Matchers;

var host = JobsHostBuilder.Create(args).Build();

// Observe failures of EVERY scheduled job centrally (a throwing job is otherwise silent until its next cron fire).
var scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
scheduler.ListenerManager.AddJobListener(
    new JobFailureListener(host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<JobFailureListener>()),
    GroupMatcher<JobKey>.AnyGroup());

await host.RunAsync();
