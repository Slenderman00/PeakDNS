#!/bin/bash

# Point to minikube's docker daemon
eval $(minikube -p minikube docker-env)

# Build directly with the right tag
docker build -t localhost:5000/peakdns:latest .