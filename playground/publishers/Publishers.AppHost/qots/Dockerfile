# Stage 1: Build the Go program
ARG GO_VERSION=1.23
FROM mcr.microsoft.com/oss/go/microsoft/golang:${GO_VERSION} AS builder
WORKDIR /app
COPY . .
RUN go build qots.go

# Stage 2: Run the Go program
FROM mcr.microsoft.com/cbl-mariner/base/core:2.0
WORKDIR /app
COPY --from=builder /app/qots .
CMD ["./qots"]
