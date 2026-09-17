# Emails

data "scaleway_tem_domain" "my_domain" {
  name = local.mail_domain
}

resource "scaleway_tem_domain" "main" {
  name       = local.mail_domain
  accept_tos = true
}

# IAM

resource "scaleway_iam_application" "mail" {
  name        = "sc-${var.environment_name}-transactional-email"
  description = "Transactional Email sending for the ${var.environment_name} API"
}

resource "scaleway_iam_policy" "mail" {
  name           = "sc-${var.environment_name}-transactional-email-policy"
  description    = "Send transactional emails from the ${var.environment_name} project"
  application_id = scaleway_iam_application.mail.id

  rule {
    project_ids          = [data.scaleway_account_project.env.id]
    permission_set_names = ["TransactionalEmailEmailSmtpCreate"]
  }
}

resource "scaleway_iam_api_key" "mail" {
  application_id     = scaleway_iam_application.mail.id
  description        = "SMTP credentials for the ${var.environment_name} API"
  default_project_id = data.scaleway_account_project.env.id
}