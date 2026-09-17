resource "scaleway_job_definition" "sdk_runner" {
  name         = "spacecraft-${var.environment_name}-sdk-runner"
  description  = "Validates, builds and publishes client SDKs from an OpenAPI spec"
  project_id   = data.scaleway_account_project.env.id

  image_uri    = "${scaleway_registry_namespace.sdk_runner.endpoint}/sdk-runner:latest"
  cpu_limit    = 1000
  memory_limit = 2048
  timeout      =

  env = {
    "ConnectionString"         = "${scaleway_sdb_sql_database.main.endpoint}&user=${scaleway_iam_api_key.database.access_key}&password=${scaleway_iam_api_key.database.secret_key}"
    "ObjectStorage__ServiceUrl" = "https://s3.fr-par.scw.cloud"
    "ObjectStorage__BucketName" = scaleway_object_bucket.openapi.name
    "ObjectStorage__Region"     = scaleway_object_bucket.openapi.region
    "ObjectStorage__AccessKey"  = scaleway_iam_api_key.object_storage.access_key
    "ObjectStorage__SecretKey"  = scaleway_iam_api_key.object_storage.secret_key
    "PublicUrls__Api"           = local.backend_url
  }

  lifecycle {
    ignore_changes = [image_uri]
  }
}