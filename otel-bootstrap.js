// safe-otel-bootstrap.js
try {
  const { NodeSDK } = require('@opentelemetry/sdk-node');
  const { getNodeAutoInstrumentations } = require('@opentelemetry/auto-instrumentations-node');
  const { OTLPTraceExporter } = require('@opentelemetry/exporter-trace-otlp-http');
  const { OTLPMetricExporter } = require('@opentelemetry/exporter-metrics-otlp-http');

  const otlpEndpoint = process.env.OTEL_EXPORTER_OTLP_ENDPOINT;
  if (!otlpEndpoint) {
    console.info('OTEL_EXPORTER_OTLP_ENDPOINT not set; OpenTelemetry bootstrap skipping exporter.');
  } else {
    const traceExporter = new OTLPTraceExporter({
      url: otlpEndpoint.endsWith('/') ? otlpEndpoint + 'v1/traces' : otlpEndpoint + '/v1/traces'
    });
    const metricExporter = new OTLPMetricExporter({
      url: otlpEndpoint.endsWith('/') ? otlpEndpoint + 'v1/metrics' : otlpEndpoint + '/v1/metrics'
    });

    const sdk = new NodeSDK({
      traceExporter,
      metricExporter,
      instrumentations: [getNodeAutoInstrumentations()]
    });

    // Start may return a Promise or undefined — handle both.
    try {
      const startResult = sdk.start();
      if (startResult && typeof startResult.then === 'function') {
        startResult
          .then(() => console.log('OpenTelemetry SDK started (async)'))
          .catch((e) => console.error('Error starting OpenTelemetry (async)', e));
      } else {
        // synchronous start (no promise)
        console.log('OpenTelemetry SDK started (sync)');
      }
    } catch (e) {
      // If start throws synchronously (rare), log and continue.
      console.error('OpenTelemetry SDK start threw synchronously', e);
    }

    // Handle shutdown similarly: may return Promise or undefined
    async function doShutdownAndExit() {
      try {
        const shutdownResult = sdk.shutdown && sdk.shutdown();
        if (shutdownResult && typeof shutdownResult.then === 'function') {
          await shutdownResult;
          console.log('OpenTelemetry SDK shut down (async)');
        } else {
          // synchronous shutdown (or no return value)
          console.log('OpenTelemetry SDK shut down (sync/no-return)');
        }
      } catch (err) {
        console.error('Error shutting down OpenTelemetry', err);
      } finally {
        // ensure process exits when asked to terminate
        try { process.exit(0); } catch (_) {}
      }
    }

    process.on('SIGTERM', () => { void doShutdownAndExit(); });
    process.on('SIGINT', () => { void doShutdownAndExit(); });
  }
} catch (_err) {
  // Missing modules or other immediate error — don't crash the process
  // (this keeps the container running even if the OTEL libs aren't present).
  // Use console.warn so logs are visible during dev.
  console.warn('OpenTelemetry bootstrap not installed or failed to load; skipping OTel initialization.');
}