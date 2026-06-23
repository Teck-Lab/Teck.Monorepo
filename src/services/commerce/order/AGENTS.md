# Order Service

## Overview
Manages the order lifecycle.

## Capabilities
- Orders
- OrderItems
- Fulfillment
- Shipping

## Events
- Emits: `OrderPlaced`, `OrderShipped`
- Consumes: `BasketCheckedOut`, `CustomerCreated`

## Database
- PostgreSQL
- EF Core migrations in-app

## Dependencies
- SharedKernel.*
- Teck.Cloud.ServiceDefaults
