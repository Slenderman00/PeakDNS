#!/bin/bash

# Build the Docker image
docker build -t peakdns .

# Run container in background and capture container ID
CONTAINER_ID=$(docker run -d -p 5354:53/udp -p 5353:54/tcp --cap-add=NET_BIND_SERVICE peakdns)

# Follow logs
docker logs -f $CONTAINER_ID

# Cleanup on script exit
trap "docker stop $CONTAINER_ID" EXIT
