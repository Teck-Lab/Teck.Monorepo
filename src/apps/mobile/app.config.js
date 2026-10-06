const app = require("./app.json");
const packageJson = require("./package.json");

const releaseVersion = process.env.TECK_RELEASE_VERSION ?? packageJson.version;
const sourceRevision = process.env.TECK_SOURCE_REVISION ?? "development";

module.exports = {
  ...app,
  expo: {
    ...app.expo,
    version: releaseVersion,
    extra: {
      ...app.expo.extra,
      releaseVersion,
      sourceRevision,
    },
  },
};
