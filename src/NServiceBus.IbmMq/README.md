# Bridging IBM MQ with NServiceBus

This sample demonstrates how to bridge IBM MQ with NServiceBus using the IBM MQ transport. You can run the demo by following the instructions below.

## Getting Started

1. Launch the IBM MQ container `docker-compose up -d`
1. (Optional) Launch the IBM MQ console `https://localhost:9443/ibmmq/console/login.html` and login with username `admin` and password `passw0rd`
1. Launch the **NServiceBus.IbmMq.Sales**, **NServiceBus.IbmMq.Shipping**, and **MessageBridgeComponent** projects
1. Within the Sales console window, press 'C' to Check Shipping Status or 'P' to Place Order
1. Close the demo and shutdown the IBM MQ container `docker-compose down`

## Solution Folders

### POC

The **POC** folder contains IBM MQ samples that demonstrate PCF commands, publish/subscribe, and transactions.

### Sample

The **Sample** folder contains the proof of concept for bridging IBM MQ with NServiceBus.


### Transport

The **Transport** folder contains the IBM MQ transport (Alpha) for NServiceBus.
