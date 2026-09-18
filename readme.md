GIT PUSH API

->Overview :

The Push API is used to publish a package from a local physical directory into a Git repository branch.
The API performs the following actions:
1. Validates the request.
2. Copies the package folder from the physical location.
3. Pushes the package into the target Git branch.
4. Maintains package metadata in 'axiPackages.json' registry file in 'aximain' branch.

->Request Payload :

{
  "physicalPath": "",
  "targetRepo": "",
  "gitBranch": "",
  "packageName": "",
  "packageDescription": "",
  "packageVersion": "",
  "packageAuthor": ""
}

->Validations :

The API validates:
1.Physical path must exist.
2.Package name should be unique and must not be empty.
3.Repository must be available and accessible.
4.Git credentials must be valid.
5.Branch will be created based on 'gitBranch' value,if it doesnot exists in the repo.
6.If 'gitBranch' is empty, the API automatically uses **aximain**.

->Repository Structure
The repository is organized using the following branch strategy:

Repository
│
├── main
│   ├── readme.md
│
├── aximain
│   ├── axiPackages.json
│   └── Package folders
│
├── Branch1
│   └── Package folders
│
├── Branch2
│   └── Package folders
│
└── ...

The **main** branch acts as the source branch for creation of new branches.
-It contains no package folders.
-Acts as a clean baseline.
-Used as the source branch when creating new branches.
-Prevents package inheritance between branches.

The **aximain** is the default package branch.It was introduced to separate package management from the repository's base branch.
-Keeps package content separate from the repository baseline.
-Serves as the default destination when no branch is given in request.
-Stores package metadata registry,'axiPackages.json'.
-Avoids leakage of packages to the new branches.

When a branch specified in the request does not exist:
1. The API creates the branch.
2. The branch is created using 'main' as the source.
3. Since main contains no package folders, the new branch starts clean.
4. The package is copied and committed only to target branch.
5. The Package details are updated in the 'axiPackages.json' registry file in the 'aximain' branch

The package registry file **axiPackages.json** is maintained only in **aximain**branch

The 'axiPackages.json' file structure :
{
  "Packages": [
    {
      "PackageName": "",
      "Branch": "",
      "PackageDescription": "",
      "PackageVersion": "",
      "PackageAuthor": "",
      "PackageCreatedOn": "",
      "Icon": "",
      "ColorClass": "",
      "Tags": [],
      "Category": ""
    }
  ],
  "GitBranches": []
}

This file acts as the central catalog of all packages available in the repository across branches.

->Push API Flow :

Request
   ↓
Validate Input
   ↓
Resolve Branch
   ↓
Clone/Open Repository
   ↓
Checkout/Create Branch
   ↓
Copy Package Folder
   ↓
Stage Changes
   ↓
Commit
   ↓
Push Branch
   ↓
Checkout aximain
   ↓
Update axiPackages.json
   ↓
Commit
   ↓
Push aximain
   ↓
Success Response
--------------------------------------------------------------------------------------------------------
GIT PULL API

->Overview :

The Pull API is used to retrieve a package from a Git repository and copy it into a specified physical location.

->Request Payload :

{
  "physicalPath": "",
  "repoURL": "",
  "packageName": "",
  "gitBranch": ""
}

->Validations :

The API validates:
1.Physical path must not be empty,it will be created locally if not exists 
2.Repository and Branch should be available and accessible.
3.Package name must exist in the given branch.
4.If Branch value in request is empty,then API takes 'aximain' as the target branch by default

->Pull API Flow

Request
   ↓
Validate Input
   ↓
Resolve Branch
   ↓
Open/Clone Repository
   ↓
Fetch Latest Changes
   ↓
Checkout Branch
   ↓
Locate Package
   ↓
Copy Package To Physical Path
   ↓
Success Response