# AxiGIT

AxiGIT publishes and retrieves Axi deliverables (Products, Change Requests, Plugins) stored in GitHub repositories, for use by AxiCmdLine.

## Language

**Account**:
The GitHub user or organization that owns a Repository.
_Avoid_: Owner, user

**Repository**:
A GitHub repository under an Account that holds Directories of a single Manager Type.
_Avoid_: Repo (in prose), Package registry

**Manager Type**:
The kind of deliverable a Repository holds: Product, ChangeRequest or Plugin.
_Avoid_: Repository type, category

**Directory**:
One deliverable within a Repository, stored as a top-level folder on the main branch.
_Avoid_: Product (when meaning the folder), Package, Item

**Version**:
One immutable published snapshot of a Directory, named `Version{N}`.
_Avoid_: Release, revision

**Latest**:
The copy of a Directory's most recently published Version, kept alongside its Versions.
_Avoid_: Current, head
