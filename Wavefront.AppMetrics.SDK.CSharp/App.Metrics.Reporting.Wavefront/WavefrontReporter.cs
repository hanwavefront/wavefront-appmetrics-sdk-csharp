﻿using System;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics.Filters;
using App.Metrics.Formatters;
using App.Metrics.Internal;
using App.Metrics.Logging;
using App.Metrics.Serialization;
using App.Metrics.Formatters.Wavefront;
using Wavefront.SDK.CSharp.Common;
using System.Collections.Generic;
using Wavefront.SDK.CSharp.Entities.Histograms;
using static Wavefront.SDK.CSharp.Common.Constants;

namespace App.Metrics.Reporting.Wavefront
{
    /// <summary>
    ///     Implementation of App Metrics reporter that handles reporting to Wavefront.
    /// </summary>
    public class WavefrontReporter : IReportMetrics
    {
        private static readonly ILog Logger = LogProvider.For<WavefrontReporter>();

        private readonly IWavefrontSender wavefrontSender;
        private readonly string source;
        private readonly IDictionary<string, string> globalTags;
        private readonly ISet<HistogramGranularity> histogramGranularities;

        public WavefrontReporter(MetricsReportingWavefrontOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (options.WavefrontSender == null)
            {
                throw new ArgumentNullException(
                    nameof(MetricsReportingWavefrontOptions.WavefrontSender));
            }

            wavefrontSender = options.WavefrontSender;

            source = options.Source;

            globalTags = new Dictionary<string, string>();
            if (options.ApplicationTags != null)
            {
                globalTags.Add(ApplicationTagKey, options.ApplicationTags.Application);
                globalTags.Add(ServiceTagKey, options.ApplicationTags.Service);
                globalTags.Add(ClusterTagKey, options.ApplicationTags.Cluster ?? NullTagValue);
                globalTags.Add(ShardTagKey, options.ApplicationTags.Shard ?? NullTagValue);
                if (options.ApplicationTags.CustomTags != null)
                {
                    foreach (var customTag in options.ApplicationTags.CustomTags)
                    {
                        globalTags.Add(customTag.Key, customTag.Value);
                    }
                }
            }

            histogramGranularities = new HashSet<HistogramGranularity>();
            if (options.WavefrontHistogram.ReportMinuteDistribution)
            {
                histogramGranularities.Add(HistogramGranularity.Minute);
            }
            if (options.WavefrontHistogram.ReportHourDistribution)
            {
                histogramGranularities.Add(HistogramGranularity.Hour);
            }
            if (options.WavefrontHistogram.ReportDayDistribution)
            {
                histogramGranularities.Add(HistogramGranularity.Day);
            }

            if (options.FlushInterval < TimeSpan.Zero)
            {
                throw new InvalidOperationException(
                    $"{nameof(MetricsReportingWavefrontOptions.FlushInterval)} " +
                    "must not be less than zero");
            }

            Filter = options.Filter;

            FlushInterval = options.FlushInterval > TimeSpan.Zero
                ? options.FlushInterval
                : AppMetricsConstants.Reporting.DefaultFlushInterval;

            // Formatting will be handled by the Wavefront sender.
            Formatter = null;

            Logger.Info($"Using Wavefront Reporter {this}. FlushInterval: {FlushInterval}");
        }

        /// <inheritdoc />
        public IFilterMetrics Filter { get; set; }

        /// <inheritdoc />
        public TimeSpan FlushInterval { get; set; }

        /// <inheritdoc />
        public IMetricsOutputFormatter Formatter { get; set; }

        /// <summary>
        ///     Flushes the current metrics snapshot in Wavefront data format.
        /// </summary>
        /// <param name="metricsData">The current snapshot of metrics.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>True if metrics were successfully flushed, false otherwise.</returns>
        public async Task<bool> FlushAsync(
            MetricsDataValueSource metricsData,
            CancellationToken cancellationToken = default)
        {
            Logger.Trace("Flushing metrics snapshot");

            try
            {
                await WriteAsync(metricsData, cancellationToken);
            }
            catch (Exception e)
            {
                Logger.Error(e.Message);
                return false;
            }

            Logger.Trace("Flushed metrics snapshot");
            return true;
        }

        /// <summary>
        ///     Writes the specified <see cref="MetricsDataValueSource" /> to the configured
        ///     <see cref="IWavefrontSender" />.
        /// </summary>
        /// <param name="metricsData">
        ///     The <see cref="MetricsDataValueSource" /> being written.
        /// </param>
        /// <param name="cancellationToken">The <see cref="CancellationToken" /></param>
        /// <returns>A <see cref="Task" /> representing the asynchronous write operation.</returns>
        private Task WriteAsync(
            MetricsDataValueSource metricsData,
            CancellationToken cancellationToken = default)
        {
            var serializer = new MetricSnapshotSerializer();

            using (var writer = new MetricSnapshotWavefrontWriter(
                wavefrontSender,
                source,
                globalTags,
                histogramGranularities))
            {
                serializer.Serialize(writer, metricsData);
            }

#if NETSTANDARD1_6
            return Task.CompletedTask;
#else
            return AppMetricsTaskHelper.CompletedTask();
#endif
        }
    }
}
