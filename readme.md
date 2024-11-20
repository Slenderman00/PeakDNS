[![Docker Build and Push](https://github.com/Slenderman00/PeakDNS/actions/workflows/docker_build_push.yaml/badge.svg)](https://github.com/Slenderman00/PeakDNS/actions/workflows/docker_build_push.yaml)
## GoodDNS
A Good DNS server

### Introduction

GoodDNS is a DNS server implemented in C#. It is developed to be as simple yet efficient as possible, relying on a simple event based architecture.
The server is mainly intended to be used as a Source Of Authority (SOA) for third level subdomains.

More information is available on my [blog](https://joar.me/blog.html#GoodDNS)


## Usage

The server expects a ```settings.ini``` file in the same directory as the base executable.

```settings.ini```
```
[logging]
#the path to the log file.
path=./log.txt
# the log level, 0 is no logging, 1 is info logging, 2 is debug logging... 5 is warning logging.
logLevel=1
[server]
#the log level, 0 is no logging, 1 is info logging, 2 is debug logging... 5 is warning logging.
port=54321
#the amount of threads allowed to be used by the TCP server.
tcpThreads=10
#the amount of threads allowed to be used by the UDP server.
udpThreads=10
[requester]
#the DNS server to use for recursive DNS queries
server=1.1.1.1
#the path to the zone files.
path=./zones
```
The server searches for zone files in the zone directory. It is worth noting that not all DNS features are supported yet.
