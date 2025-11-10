// production-ready OpenTelemetry ESM bootstrap for Node.js
// - uses OTLP HTTP exporters when OTEL_EXPORTER_OTLP_ENDPOINT is set
// - falls back to Console exporters when OTLP packages are not present (safe for dev)
// - installs a PeriodicExportingMetricReader for metrics
// - sets up graceful shutdown on SIGTERM/SIGINT and process errors

import { NodeSDK } from '@opentelemetry/sdk-node';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';
import { PeriodicExportingMetricReader, ConsoleMetricExporter } from '@opentelemetry/sdk-metrics';
import { ConsoleSpanExporter } from '@opentelemetry/sdk-trace-node';
// @opentelemetry/resources and semantic-conventions are CommonJS packages in some
// releases; import as default and destructure to remain compatible with both CJS and ESM
import resourcesPkg from '@opentelemetry/resources';
import semanticPkg from '@opentelemetry/semantic-conventions';
const { Resource } = resourcesPkg;
const { SemanticResourceAttributes } = semanticPkg;

// Helpers to create exporters. We attempt to load OTLP exporters dynamically so the
// module can be imported even when the OTLP packages are not installed in dev.
async function createExporters() {
  const endpoint = process.env.OTEL_EXPORTER_OTLP_ENDPOINT;
  let traceExporter = null;
  let metricExporter = null;

  if (endpoint) {
    try {
      // dynamic imports so missing packages don't crash consumers who don't install them
      const traceMod = await import('@opentelemetry/exporter-trace-otlp-http');
      const metricsMod = await import('@opentelemetry/exporter-metrics-otlp-http');

      const tracesUrl = endpoint.endsWith('/') ? `${endpoint}v1/traces` : `${endpoint}/v1/traces`;
      const metricsUrl = endpoint.endsWith('/') ? `${endpoint}v1/metrics` : `${endpoint}/v1/metrics`;

      const OTLPTraceExporter = traceMod.OTLPTraceExporter ?? traceMod.default ?? traceMod;
      const OTLPMetricExporter = metricsMod.OTLPMetricExporter ?? metricsMod.default ?? metricsMod;

      traceExporter = new OTLPTraceExporter({ url: tracesUrl });
      metricExporter = new OTLPMetricExporter({ url: metricsUrl });

      console.info('OpenTelemetry: configured OTLP exporters for', endpoint);
    } catch (err) {
      console.warn('OpenTelemetry: failed to load OTLP exporters, falling back to Console exporters.', err?.message ?? err);
    }
  } else {
    console.info('OpenTelemetry: OTEL_EXPORTER_OTLP_ENDPOINT not set; using Console exporters');
  }

  // Fallbacks
  if (!traceExporter) traceExporter = new ConsoleSpanExporter();
  if (!metricExporter) metricExporter = new ConsoleMetricExporter();

  return { traceExporter, metricExporter };
}

// Build a Resource including service name if provided via env
function createResource() {
  const serviceName = process.env.OTEL_SERVICE_NAME;
  if (serviceName) {
    return new Resource({ [SemanticResourceAttributes.SERVICE_NAME]: serviceName });
  }
  return undefined;
}

(async () => {
  try {
    const { traceExporter, metricExporter } = await createExporters();

    const metricReader = new PeriodicExportingMetricReader({ exporter: metricExporter });

    const sdkOptions = {
      traceExporter,
      metricReader,
      instrumentations: [getNodeAutoInstrumentations()],
    };

    const resource = createResource();
    if (resource) sdkOptions.resource = resource;

    const sdk = new NodeSDK(sdkOptions);

    await sdk.start();
    console.info('OpenTelemetry SDK started');

    // Graceful shutdown
    async function shutdown(code = 0) {
      try {
        await sdk.shutdown();
        console.info('OpenTelemetry SDK shut down');
      } catch (err) {
        console.error('Error shutting down OpenTelemetry SDK', err);
        code = code || 1;
      } finally {
        try { process.exit(code); } catch (_) { /* ignore */ }
      }
    }

    process.on('SIGTERM', () => { void shutdown(0); });
    process.on('SIGINT', () => { void shutdown(0); });
    process.on('beforeExit', () => { void shutdown(0); });
    process.on('uncaughtException', (err) => {
      console.error('Uncaught exception — shutting down OpenTelemetry SDK', err);
      void shutdown(1);
    });
    process.on('unhandledRejection', (reason) => {
      console.error('Unhandled promise rejection — shutting down OpenTelemetry SDK', reason);
      void shutdown(1);
    });
  } catch (err) {
    // Make sure we don't crash the process if instrumentation fails to initialize
    console.warn('OpenTelemetry bootstrap failed to initialize; continuing without SDK.', err?.message ?? err);
  }
})();
