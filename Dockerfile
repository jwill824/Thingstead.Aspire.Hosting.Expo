FROM node:24

ARG EXPO_PORT=8082
ENV EXPO_PORT=${EXPO_PORT}

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl ca-certificates procps git

WORKDIR /app

COPY . /app

RUN if [ -f package-lock.json ]; then npm ci --no-audit --no-fund; else npm install --no-audit --no-fund; fi

COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod +x /usr/local/bin/docker-entrypoint.sh

COPY otel-bootstrap.js /app/otel-bootstrap.js

EXPOSE ${EXPO_PORT}

ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
