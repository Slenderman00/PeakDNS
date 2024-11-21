#!/bin/bash

# Build the Docker image
docker build --no-cache -t peakdns .

# Run container and follow logs
docker run --rm -p 8053:53/udp -p 8053:53/tcp --cap-add=NET_BIND_SERVICE peakdns