﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Crawler.Downloader;
using Crawler.Logger;
using Crawler.Pipelines;

namespace Crawler
{
    public class Crawler : ICrawler
    {
        private readonly IEnumerable<Site> _sites;
        private DateTime _beginTime;
        private DateTime _endTime;
        private IEnumerable<IPipeline> _pipelines;
        private int _threadNum;
        private string _named;
        private PipelineRunMode _runMode;
        private Task _pipelineStatusTask;
        private IReporter _reporter;

        public Crawler()
        {
        }

        public Crawler(string name, IEnumerable<Site> sites, IEnumerable<IPipeline> pipelines)
        {
            Name = name;
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _pipelines = pipelines ?? throw new ArgumentNullException(nameof(pipelines));
        }

        public Crawler(IEnumerable<Site> sites, IEnumerable<IPipeline> pipelines) : this(Guid.NewGuid().ToString("N"), sites,
            pipelines)
        {
        }

        public string Name
        {
            get => _named;
            set
            {
                _named = value;
                Logger = LoggerManager.GetLogger(_named);
            }
        }

        public int ThreadNum
        {
            get => _threadNum;
            set
            {
                if (CheckState(CrawlerState.Running))
                    throw new InvalidOperationException("爬虫正在运行。");

                if (value < 0)
                    throw new ArgumentException("爬虫线程数量不能小于0。");
                _threadNum = value;
            }
        }

        public PipelineRunMode RunMode
        {
            get => _runMode;
            set
            {
                if (CheckState(CrawlerState.Running))
                    throw new InvalidOperationException("爬虫正在运行。");
                _runMode = value;
            }
        }

        public IReporter Reporter
        {
            get => _reporter ?? (_reporter = CreateDefaultReporter());
            set => _reporter = value ?? throw new InvalidOperationException("Reporter不可设置为null.");
        }

        protected virtual IReporter CreateDefaultReporter()
        {
            return new NLoggerReporter(_pipelines, Schedulers.SchedulerManager.GetAllScheduler());
        }

        public CrawlerState CrawlerState { get; protected set; }

        public ILogger Logger { get; protected set; }

        public IEnumerable<IPipeline> Pipelines
        {
            get => _pipelines;
            set
            {
                if (CheckState(CrawlerState.Running))
                    throw new InvalidOperationException("爬虫正在运行。");
                _pipelines = value;
            }
        }

        public void Pause()
        {
            if (CrawlerState == CrawlerState.Running)
                CrawlerState = CrawlerState.Stopped;
        }

        public void Continue()
        {
            if (CrawlerState == CrawlerState.Stopped)
                CrawlerState = CrawlerState.Running;
        }

        public void Exit()
        {
            CrawlerState = CrawlerState.Exited;
        }

        public void Run()
        {
            if (CrawlerState == CrawlerState.Running)
                return;

            CrawlerState = CrawlerState.Running;
            _beginTime = DateTime.Now;

            _pipelineStatusTask = Task.Factory.StartNew(() =>
            {
                while (true)
                {
                    // 报告状态。
                    Reporter.ReportStatus();
                }
            });

            while (CrawlerState == CrawlerState.Running || CrawlerState == CrawlerState.Stopped)
            {
                if (CrawlerState == CrawlerState.Stopped)
                {
                    Thread.Sleep(500);
                    continue;
                }

                Parallel.For(0, ThreadNum, new ParallelOptions
                {
                    MaxDegreeOfParallelism = ThreadNum
                }, i =>
                {
                    while (CrawlerState == CrawlerState.Running)
                    {
                        if (Pipelines.All(x => x.IsComplete))
                        {
                            CrawlerState = CrawlerState.Finished;
                            break;
                        }

                        var context = new PipelineContext
                        {
                            Crawler = this,
                            Configuration = new CrawlerConfiguration
                            {
                                Crawler = this,
                                Pipelines = Pipelines,
                                StartSites = _sites,
                                ThreadNum = _threadNum
                            }
                        };

                        try
                        {
                            if (RunMode == PipelineRunMode.Chain)
                            {
                                Pipelines.FirstOrDefault()?.ExecuteAsync(context).GetAwaiter().GetResult();
                            }
                            else if(RunMode == PipelineRunMode.Parallel)
                            {
                                Task.WaitAny(Pipelines.Select(pipeline =>
                                    pipeline.ExecuteAsync((PipelineContext) context.Clone())).ToArray());
                            }
                        }
                        catch (Exception exception)
                        {
                            Logger.Error(exception.Message, exception);
                        }
                    }
                });
            }

            _pipelineStatusTask.Wait(500);
            _endTime = DateTime.Now;
            Logger?.Info("总耗时（s）：" + (_endTime - _beginTime).TotalSeconds);
        }

        public Task RunAsync()
        {
            return Task.Factory.StartNew(Run);
        }

        private bool CheckState(CrawlerState state)
        {
            return CrawlerState == state;
        }

        protected virtual void ReportStatus()
        {
            Logger.Info($"Pipeline Mode:{RunMode}, Pipelines:{Pipelines}, Completed Pipeline:{1}");
            foreach (var schedulerDic in Schedulers.SchedulerManager.GetAllScheduler())
            {
                foreach (var item in schedulerDic)
                {
                    Logger.Info($"Scheduler:{item.Key}, Total:{item.Value.Count}, Completed:{1}");
                }
            }
        }
    }
}