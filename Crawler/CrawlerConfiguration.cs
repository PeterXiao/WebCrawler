﻿using System.Collections.Generic;
using Crawler.Pipelines;

namespace Crawler
{
    public class CrawlerConfiguration
    {
        public IEnumerable<Site> StartSites { get; set; }
        public int ThreadNum { get; set; }
        public IPipeline Pipeline { get; set; }
    }
}