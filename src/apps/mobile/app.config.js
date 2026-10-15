const packageJson = require("./package.json");

const releaseVersion = process.env.TECK_RELEASE_VERSION ?? packageJson.version;
const sourceRevision = process.env.TECK_SOURCE_REVISION ?? "development";

module.exports = ({ config }) => ({
  ...config,
  version: releaseVersion,
  extra: {
    ...config.extra,
    releaseVersion,
    sourceRevision,
  },
});
