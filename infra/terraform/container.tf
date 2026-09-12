resource "scaleway_container_namespace" "main" {
  name = "spacecraft-ns"
}

resource "scaleway_container" "backend" {
  name                   = "spacecraft-api"
  description            = "SpaceCraft API — generates and publishes client SDKs from OpenAPI specs"
  tags                   = [var.environment_name, "backend", "api"]
  namespace_id           = scaleway_container_namespace.main.id
  image                  = "alpine:latest"       // placeholder image
  port                   = 80

  cpu_limit              = 500
  memory_limit_bytes     = 1024000000
  min_scale              = 1
  max_scale              = 5
  timeout                = 300
  protocol               = "h2c"                 // protocol HTTP/2
  https_connections_only = true
  
  secret_environment_variables = {
    "ObjectStorage__AccessKey" = scaleway_iam_api_key.object_storage.access_key
    "ObjectStorage__SecretKey" = scaleway_iam_api_key.object_storage.secret_key
    "ConnectionString"         = "Host=${scaleway_sdb_sql_database.main.endpoint};Port=5432;Database=${scaleway_sdb_sql_database.main.name};Username=${scaleway_sdb_sql_database.main.id};Password=${scaleway_iam_api_key.database.secret_key}"
  }
  
  lifecycle {
    ignore_changes = [image]
  }
}