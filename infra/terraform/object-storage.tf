// Object storage

resource "scaleway_object_bucket" "openapi" {
  name   = "spacecraft-${var.environment_name}-openapi"
  region = "fr-par"
}

resource "scaleway_object_bucket_acl" "openapi" {
  bucket = scaleway_object_bucket.openapi.id
  acl    = "private"
}

resource "scaleway_object_bucket_server_side_encryption_configuration" "openapi" {
  bucket = scaleway_object_bucket.openapi.id
  
  rule {
    apply_server_side_encryption_by_default {
      sse_algorithm = "AES256"
    }
  }
}

// IAM

resource "scaleway_iam_application" "object_storage" {
  name        = "sc-${var.environment_name}-object-storage"
  description = "Object Storage access for the ${var.environment_name} workloads"
}

resource "scaleway_iam_policy" "object_storage" {
  name           = "sc-${var.environment_name}-object-storage-policy"
  description    = "Read, write and delete objects in the ${var.environment_name} project"
  application_id = scaleway_iam_application.object_storage.id

  rule {
    project_ids          = [data.scaleway_account_project.env.id]
    permission_set_names = ["ObjectStorageObjectsRead", "ObjectStorageObjectsWrite", "ObjectStorageObjectsDelete"]
  }
}

resource "scaleway_iam_api_key" "object_storage" {
  application_id     = scaleway_iam_application.object_storage.id
  description        = "Object Storage key for ${var.environment_name}"
  default_project_id = data.scaleway_account_project.env.id
}
