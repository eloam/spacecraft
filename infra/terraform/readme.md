# OpenTofu

Infrastructure as code for Scaleway, managed with [OpenTofu](https://opentofu.org).
The binary is `tofu`; the configuration language, the `.tf` files, the state
format and the `s3` backend are the same as Terraform's.

## Layout

```
terraform/
  modules/      reusable modules, not deployed on their own
  iam/          IAM applications and policies (organization level)
  staging/      staging project
  prod/         production project — deployed from CI only
```

Each top-level directory is a separate deployment unit: its own state, its own
Scaleway credentials. Run OpenTofu from inside one of them, never from the root.

## Prerequisites

### OpenTofu

```bash
brew install opentofu        # macOS
```

Debian / Ubuntu / WSL, with the official installer:

```bash
curl --proto '=https' --tlsv1.2 -fsSL https://get.opentofu.org/install-opentofu.sh -o install-opentofu.sh
chmod +x install-opentofu.sh
./install-opentofu.sh --install-method deb
rm -f install-opentofu.sh
```

Check it works:

```bash
tofu -version
```

Do not install Terraform alongside it: both tools write the same `.terraform/`
directory and `.terraform.lock.hcl`, and mixing them corrupts the provider cache.

### Scaleway CLI

```bash
brew install scw          # macOS
# or see https://github.com/scaleway/scaleway-cli
```

Create one profile per IAM application. You will be asked for the access key,
the secret key and the organization ID:

```bash
scw init --profile sc-terraform-iam
scw init --profile sc-terraform-staging
```

Secret keys are shown only once in the Scaleway console. The CLI stores them in
`~/.config/scw/config.yaml`, which is where the `.envrc` files read them from.

### direnv

`direnv` is a third-party tool — **it is not built into your shell** and must be
installed separately. It loads the environment variables of a directory when you
`cd` into it, and unloads them when you leave, so the credentials in use are
always the ones matching the directory you are in.

```bash
brew install direnv          # macOS
sudo apt install direnv      # Debian / Ubuntu / WSL
```

Then hook it into your shell. The line must go at the **end** of the file, after
any framework such as Oh My Zsh:

```bash
echo 'eval "$(direnv hook zsh)"' >> ~/.zshrc   # bash: direnv hook bash >> ~/.bashrc
source ~/.zshrc
```

Check it works:

```bash
direnv version
```

Windows users: use WSL. direnv has no native PowerShell equivalent.

## Setup

`.envrc` files are gitignored. Create yours from the examples:

```bash
cp .envrc.example .envrc
cp iam/.envrc.example iam/.envrc
cp staging/.envrc.example staging/.envrc
```

Fill in the values, then approve each file once:

```bash
direnv allow .
direnv allow iam
direnv allow staging
```

`direnv allow` registers a hash of the file's contents. Any edit revokes the
approval and you have to run it again — this is intentional.

## Usage

```bash
cd staging
tofu init
tofu plan
tofu apply
```

`tofu plan` is the dry run: it reads the real state and shows what would
change without writing anything.

## Migrating a checkout that was initialised with Terraform

If you ran `terraform init` in one of these directories before the switch:

```bash
rm -rf .terraform
tofu init
tofu plan
```

`tofu init` resolves the provider through the OpenTofu registry and updates
`.terraform.lock.hcl`; commit that change. `tofu plan` must report no changes:
the remote state is read as is, nothing is migrated.

## Notes

- Production credentials are never stored locally. `prod/` is deployed from CI,
  using the secrets of the `prod` GitHub environment.
- The `s3` backend is an AWS client, so it only reads `AWS_*` variables. The
  Scaleway provider reads `SCW_*`. The `.envrc` files export the same key under
  both names.
- Deploy `iam/` before `staging/` or `prod/`: it creates the IAM applications and
  API keys the other stacks consume.
- OpenTofu keeps Terraform's file and variable names: `.terraform/`,
  `.terraform.lock.hcl`, `terraform.tfvars` and `TF_VAR_*` all work unchanged.
