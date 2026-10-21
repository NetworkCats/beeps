variable "region" {
  description = "Vultr region id (https://api.vultr.com/v2/regions): lax is Los Angeles."
  type        = string
  default     = "lax"
}

variable "plan" {
  description = "Vultr plan id: High Performance AMD, 2 vCPU, 2 GB."
  type        = string
  default     = "vhp-2c-2gb-amd"
}

variable "hostname" {
  description = "The server's host name. Changing it reinstalls the server on Vultr."
  type        = string
  default     = "beeps"
}

variable "ssh_public_keys" {
  description = "Public keys allowed to log in as deploy, one per line: the GitHub Actions deploy key and your own."
  type        = string
  # Kept out of plans: a public key lets anyone ask a server whether it accepts it, which would find the origin.
  sensitive = true

  validation {
    condition     = length(compact(split("\n", var.ssh_public_keys))) > 0
    error_message = "Give at least one public key."
  }
}

variable "backups" {
  description = "Vultr's automatic daily backups of the whole server (20% of its price). The database lives on it."
  type        = bool
  default     = true
}
