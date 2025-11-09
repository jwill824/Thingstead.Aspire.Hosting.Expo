// otel-bootstrap.js
const { NodeSDK } = require('@opentelemetry/sdk-node');
const { getNodeAutoInstrumentations } = require('@opentelemetry/auto-instrumentations-node');
const { OTLPTraceExporter } = require('@opentelemetry/exporter-trace-otlp-http');
const { OTLPMetricExporter } = require('@opentelemetry/exporter-metrics-otlp-http');
const process = require('process');

const otlpEndpoint = process.env.OTEL_EXPORTER_OTLP_ENDPOINT;
if (!otlpEndpoint) {
  // nothing configured
  console.warn('OTEL_EXPORTER_OTLP_ENDPOINT not set; OpenTelemetry bootstrap skipping exporter.');
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

  sdk.start()
    .then(() => console.log('OpenTelemetry SDK started'))
    .catch((e) => console.error('Error starting OpenTelemetry', e));

  process.on('SIGTERM', () => {
    sdk.shutdown()
       .then(() => console.log('OpenTelemetry SDK shut down'))
       .catch((e) => console.error('Error shutting down OpenTelemetry', e))
       .finally(() => process.exit());
  });
}