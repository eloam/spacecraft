resource "scaleway_container_namespace" "main" {
  name = "spacecraft-app"
}

resource "scaleway_container" "backend" {
  name         = "spacecraft-api"
  description  = "SpaceCraft API — generates and publishes client SDKs from OpenAPI specs"
  tags         = [var.environment_name, "backend", "api"]
  namespace_id = scaleway_container_namespace.main.id
  image        = "alpine:latest" # placeholder image
  port         = 80

  cpu_limit              = 500
  memory_limit_bytes     = 1024000000
  min_scale              = 1
  max_scale              = 5
  timeout                = 300
  protocol               = "h2c" # protocol HTTP/2
  https_connections_only = true


  environment_variables = {
    "Jwt__Issuer"   = local.backend_url # e.g. https://staging-api.spacecraft.sh
    "Jwt__Audience" = local.backend_url

    "ObjectStorage__ServiceUrl" = "https://s3.fr-par.scw.cloud"
    "ObjectStorage__BucketName" = scaleway_object_bucket.openapi.name
    "ObjectStorage__Region"     = scaleway_object_bucket.openapi.region

    "PublicUrls__Api" = local.api_url
    "PublicUrls__App" = local.app_url

    "Email__Smtp__Host"      = local.smtp_host
    "Email__Smtp__Port"      = tostring(local.smtp_port)
    "Email__Smtp__Security"  = local.smtp_security
    "Email__Smtp__FromEmail" = local.smtp_from_email
    "Email__Smtp__FromName"  = var.smtp_from_name
  }

  secret_environment_variables = {
    "ConnectionString"         = "${scaleway_sdb_sql_database.main.endpoint}&user=${scaleway_iam_api_key.database.access_key}&password=${scaleway_iam_api_key.database.secret_key}"
    "Jwt__SigningKey"          = random_password.jwt_signing_key.result
    "ObjectStorage__AccessKey" = scaleway_iam_api_key.object_storage.access_key
    "ObjectStorage__SecretKey" = scaleway_iam_api_key.object_storage.secret_key
    # PieplineJobs --TODO
    "Email__Smtp__Username" = scaleway_iam_api_key.mail.access_key
    "Email__Smtp__Password" = scaleway_iam_api_key.mail.secret_key
  }

  lifecycle {
    ignore_changes = [image]
  }
}