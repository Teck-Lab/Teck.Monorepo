const { VersionActions } = require("nx/release");

/**
 * Nx release adapter for .NET projects whose version is supplied at build time.
 * Nx owns SemVer calculation; no csproj file is rewritten in the release PR.
 */
class DotnetVersionActions extends VersionActions {
  validManifestFilenames = null;

  async readCurrentVersionFromSourceManifest() {
    return {
      currentVersion: "0.0.0",
      manifestPath: "Nx first-release baseline",
    };
  }

  async readCurrentVersionFromRegistry() {
    return null;
  }

  async readCurrentVersionOfDependency() {
    return {
      currentVersion: null,
      dependencyCollection: null,
    };
  }

  async updateProjectVersion(_tree, newVersion) {
    return [`Resolved build-time .NET version ${newVersion}; no source manifest changed.`];
  }

  async updateProjectDependencies() {
    return [];
  }
}

module.exports = DotnetVersionActions;
