﻿namespace Services.Redmine
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Collections.Generic;
    using System.Collections.Specialized;

    using NLog;

    using Framework.Timeline;

    using RedmineApi.Core;
    using RedmineApi.Core.Types;

    using Tasker.Common.Task;
    using Tasker.Interfaces.Task;

    public class RedmineService : ITaskService, ITaskVisitor, IDisposable
    {
        #region Fields

        private readonly ILogger _logger;

        private readonly CancellationTokenSource _cancellationSource;

        private readonly TaskQueue _queue;

        private readonly Dictionary<int, Issue> _issues;
        private readonly Dictionary<TaskState, IssueStatus> _statuses;

        private IRedmineProxy _proxy;

        #endregion Fields

        #region Events

        public event Action<object, ITaskCommon, IEnumerable<string>> Notify;

        #endregion Events

        #region Properties

        public IRedmineOptions Options { get; }

        #endregion Properties

        #region Constructors

        public RedmineService(IRedmineOptions options, ITimelineEnvironment timeline, IRedmineProxy proxy) 
            : this(options, timeline)
        {
            _proxy = proxy;
        }

        public RedmineService(IRedmineOptions options, ITimelineEnvironment timeline)
        {
            _logger = LogManager.GetCurrentClassLogger();

            Options = options;

            _issues = new Dictionary<int, Issue>();
            _statuses = new Dictionary<TaskState, IssueStatus>();

            _cancellationSource = new CancellationTokenSource();
            _queue = new TaskQueue(task => task.Handle(this), timeline);
            _queue.Error += (task, error) => _logger?.Error($"failed task: {task}, error: `{error}`"); ;
        }

        public void Dispose()
        {
            Stop();
            _proxy?.Dispose();
        }

        #endregion Constructors

        #region Methods

        public void Start()
        {
            if (_queue.HasEnabled())
                return;

            _proxy ??= new RedmineProxy(Options.Host, Options.ApiKey, MimeType.Xml, DefaultRedmineHttpSettings.Create());
            _queue.Start();

            Enqueue(new SyncActionTask(SyncStatuses));
            Enqueue(new SyncActionTask(SyncIssues, _queue, Options.Sync.Interval));
        }

        public void Stop()
        {
            if (!_queue.HasEnabled())
                return;

            _cancellationSource.Cancel();
            _queue.Stop();
        }

        public void Enqueue(ITaskItem task)
        {
            _queue.Enqueue(task);
        }

        public string Handle(ITaskCommon task)
        {
            if (string.IsNullOrWhiteSpace(task.ExternalId))
            {
                return string.Empty;
            }

            Issue issue = RunAsync(() => _proxy.Get<Issue>(task.ExternalId.ToString(), new NameValueCollection()));
            if (issue == null)
            {
                return string.Empty;
            }

            _statuses.TryGetValue(task.Context.Status, out var status);
            issue.Status = status;

            //if (issue.EstimatedHours == null)
            //    issue.EstimatedHours = Options.EstimatedHoursABS;

            _issues[issue.Id] = issue;

            //var values1 = new NameValueCollection() { { RedmineKeys.ISSUE_ID, issue.Id.ToString() } };
            //var updates1 = RunAsync(() => _proxy.ListAll<TimeEntry>(values1));

            if (status != null)
            {
                issue = RunAsync(() => _proxy.Update(task.ExternalId.ToString(), issue));
            }

            return issue.Id.ToString();
        }

        public void WaitSync() => _queue.IsEmpty();

        private bool SyncStatuses()
        {
            var updates = RunAsync(() => _proxy.ListAll<IssueStatus>(new NameValueCollection()));

            foreach (var status in updates)
            {
                if (!Enum.TryParse<TaskState>(status.Name.Replace(" ", string.Empty), true, out var state))
                    continue;

                _statuses[state] = status;
            }

            return true;
        }

        private bool SyncIssues()
        {
            var values = new NameValueCollection() { { RedmineKeys.ASSIGNED_TO_ID, Options.Sync.UserId.ToString() } };
            List<Issue> updates = RunAsync(() => _proxy.ListAll<Issue>(values));

            foreach (Issue issue in updates)
            {
                if (_issues.ContainsKey(issue.Id) && Equals(_issues[issue.Id], issue))
                    continue;

                _issues[issue.Id] = issue;

                Notify?.Invoke(this, 
                    new TaskCommon
                    {
                        ExternalId = issue.Id.ToString(),
                        Context = new TaskContext
                        {
                            Id = issue.Id.ToString(),
                            Name = issue.Subject,
                            Description = issue.Description,
                            Kind = Enum.TryParse<TaskKind>(issue.Tracker.Name, out var kind) ? kind : TaskKind.Task,
                            Status = Enum.TryParse<TaskState>(issue.Status.Name.Replace(" ", string.Empty), true, out var state) ? state : TaskState.New,
                        }
                    },
                    new string[]
                    {
                        nameof(TaskContext.Id),
                        nameof(TaskContext.Name),
                        nameof(TaskContext.Description),
                        nameof(TaskContext.Kind),
                        nameof(TaskContext.Status),
                    });
            }

            return true;
        }

        private bool Equals(Issue source, Issue target) 
        {
            return
                source.Id == target.Id &&
                source.Subject == target.Subject &&
                source.Description == target.Description &&
                //source.EstimatedHours == target.EstimatedHours &&
                //source.SpentHours == target.SpentHours &&
                source.Status?.Id == target.Status?.Id;
        }

        private T RunAsync<T>(Func<Task<T>> action)
        {
            return Task.Run(action, _cancellationSource.Token).Result;
        }

        #endregion Methods
    }
}
