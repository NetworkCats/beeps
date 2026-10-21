terraform {
  required_version = ">= 1.10"

  required_providers {
    vultr = {
      source  = "vultr/vultr"
      version = "~> 2.33"
    }
  }

  # State in Cloudflare R2, through its S3 API, locked with a lock file beside it (R2 supports conditional writes).
  # The bucket and endpoint come from backend.hcl (backend.hcl.example), the R2 key from AWS_ACCESS_KEY_ID and
  # AWS_SECRET_ACCESS_KEY.
  backend "s3" {
    key                         = "beeps/production.tfstate"
    region                      = "auto"
    use_path_style              = true
    use_lockfile                = true
    skip_credentials_validation = true
    skip_region_validation      = true
    skip_requesting_account_id  = true
    skip_metadata_api_check     = true
    skip_s3_checksum            = true
  }
}
