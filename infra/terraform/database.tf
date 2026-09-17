# Database

resource "scaleway_sdb_sql_database" "main" {
  name    = "spacecraft-${var.environment_name}-database"
  min_cpu = 0
  max_cpu = 4
}

# IAM

resource "scaleway_iam_application" "database" {
  name        = "sc-${var.environment_name}-database"
  description = "Serverless SQL access for the ${var.environment_name} API and migrations"
}

resource "scaleway_iam_policy" "database" {
  name           = "sc-${var.environment_name}-database-policy"
  description    = "Query and alter the schema of the ${var.environment_name} Serverless SQL databases"
  application_id = scaleway_iam_application.database.id

  rule {
    project_ids          = [data.scaleway_account_project.env.id]
    permission_set_names = ["ServerlessSQLDatabaseReadWrite"]
  }
}

resource "scaleway_iam_api_key" "database" {
  application_id     = scaleway_iam_application.database.id
  description        = "Serverless SQL key for the ${var.environment_name} API"
  default_project_id = data.scaleway_account_project.env.id
}