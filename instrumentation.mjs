// production-ready OpenTelemetry ESM bootstrap for Node.js
// - uses OTLP HTTP exporters when OTEL_EXPORTER_OTLP_ENDPOINT is set
// - falls back to Console exporters when OTLP packages are not present (safe for dev)
// - installs a PeriodicExportingMetricReader for metrics
// - sets up graceful shutdown on SIGTERM/SIGINT and process errors

import { NodeSDK } from '@opentelemetry/sdk-node';
import { getNodeAutoInstrumentations } from '@opentelemetry/auto-instrumentations-node';
import { PeriodicExportingMetricReader, ConsoleMetricExporter } from '@opentelemetry/sdk-metrics';
import { ConsoleSpanExporter } from '@opentelemetry/sdk-trace-node';
// @opentelemetry/resources and semantic-conventions can export in different shapes
// depending on the package release (CommonJS vs ESM). Import the package as a
// default and resolve the actual constructors/objects at runtime to remain robust.
import resourcesPkg from '@opentelemetry/resources';
import semanticPkg from '@opentelemetry/semantic-conventions';

function resolveSemanticAttrs(pkg) {
  if (!pkg) return {};
  return pkg.SemanticResourceAttributes ?? pkg.default?.SemanticResourceAttributes ?? pkg.default ?? pkg;
}

const SemanticResourceAttributes = resolveSemanticAttrs(semanticPkg);

function resolveResourceCtor(pkg) {
  if (!pkg) return null;
  const candidates = [pkg, pkg.default, pkg.Resource, pkg.default?.Resource];
  for (const c of candidates) {
    if (typeof c === 'function') return c;
  }
  return null;
}

const ResourceCtor = resolveResourceCtor(resourcesPkg);

// Helpers to create exporters. We attempt to load OTLP exporters dynamically so the
// module can be imported even when the OTLP packages are not installed in dev.
async function createExporters() {
  const endpoint = process.env.OTEL_EXPORTER_OTLP_ENDPOINT;
  let traceExporter = null;
  let metricExporter = null;
  if (endpoint) {
    // Determine preferred protocol: explicit env var takes precedence
    const preferredProtocol = (process.env.OTEL_EXPORTER_OTLP_TRACES_PROTOCOL || process.env.OTEL_EXPORTER_OTLP_PROTOCOL || '').toLowerCase();
    const useGrpc = preferredProtocol === 'grpc' || preferredProtocol === 'grpc-web' || endpoint.startsWith('grpc://') || endpoint.endsWith(':4317');

    // Try gRPC exporters first when indicated, otherwise try HTTP exporters
    if (useGrpc) {
      try {
        const traceMod = await import('@opentelemetry/exporter-trace-otlp-grpc');
        const metricsMod = await import('@opentelemetry/exporter-metrics-otlp-grpc');
        const OTLPTraceExporter = traceMod.OTLPTraceExporter ?? traceMod.default ?? traceMod;
        const OTLPMetricExporter = metricsMod.OTLPMetricExporter ?? metricsMod.default ?? metricsMod;

        // For gRPC exporters the endpoint is usually host:port (without scheme)
        // Strip scheme if present
        const grpcEndpoint = endpoint.replace(/^https?:\/\//, '').replace(/^grpc:\/\//, '');
        traceExporter = new OTLPTraceExporter({ url: grpcEndpoint });
        metricExporter = new OTLPMetricExporter({ url: grpcEndpoint });
        console.info('OpenTelemetry: configured OTLP gRPC exporters for', grpcEndpoint);
      } catch (err) {
        console.warn('OpenTelemetry: failed to load OTLP gRPC exporters, falling back to HTTP exporters.', err?.message ?? err);
      }
    }

    // If gRPC wasn't selected or failed, try HTTP exporters
    if (!traceExporter || !metricExporter) {
      try {
        const traceMod = await import('@opentelemetry/exporter-trace-otlp-http');
        const metricsMod = await import('@opentelemetry/exporter-metrics-otlp-http');

        const tracesUrl = endpoint.endsWith('/') ? `${endpoint}v1/traces` : `${endpoint}/v1/traces`;
        const metricsUrl = endpoint.endsWith('/') ? `${endpoint}v1/metrics` : `${endpoint}/v1/metrics`;

        const OTLPTraceExporter = traceMod.OTLPTraceExporter ?? traceMod.default ?? traceMod;
        const OTLPMetricExporter = metricsMod.OTLPMetricExporter ?? metricsMod.default ?? metricsMod;

        // Only set if not already set by gRPC attempt
        if (!traceExporter) traceExporter = new OTLPTraceExporter({ url: tracesUrl });
        if (!metricExporter) metricExporter = new OTLPMetricExporter({ url: metricsUrl });

        console.info('OpenTelemetry: configured OTLP HTTP exporters for', endpoint);
      } catch (err) {
        console.warn('OpenTelemetry: failed to load OTLP HTTP exporters, falling back to Console exporters.', err?.message ?? err);
      }
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
    if (ResourceCtor) {
      return new ResourceCtor({ [SemanticResourceAttributes.SERVICE_NAME]: serviceName });
    }
    // If we couldn't resolve the Resource constructor, return an object-shaped resource
    // The SDK will accept a Resource instance; if not available, fall back to undefined
    try {
      return { attributes: { [SemanticResourceAttributes.SERVICE_NAME]: serviceName } };
    } catch {
      return undefined;
    }
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
    if (resource && ResourceCtor) {
      sdkOptions.resource = resource;
      console.info('OpenTelemetry: using Resource instance for service.name');
    } else if (resource && !ResourceCtor) {
      // We resolved attributes but couldn't construct a Resource instance — log and skip
      console.warn('OpenTelemetry: Resource constructor unavailable; skipping setting SDK resource.');
    }

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
