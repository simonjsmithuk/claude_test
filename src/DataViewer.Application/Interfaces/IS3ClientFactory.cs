// This file is intentionally omitted from the Application layer.
// IS3ClientFactory references Amazon.S3.AmazonS3Client, which is an AWS SDK type
// and therefore an Infrastructure concern. The factory is declared as an
// Infrastructure-internal interface in DataViewer.Infrastructure.S3.IS3ClientFactory.
// The Application layer only knows IS3Service (this file) — not the factory.
