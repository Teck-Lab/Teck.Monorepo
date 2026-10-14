import { readFileSync, writeFileSync } from "node:fs";
import { pathToFileURL } from "node:url";
import { readManifest } from "./platform-release.mjs";
export function releaseServices(manifest, services) {
  return services.flatMap((service) => {
    const unit = manifest.components.find(
      (component) => component.releaseGroup === service.releaseGroup,
    );
    if (!unit)
      throw new Error(`Deployable service ${service.product} is missing from Nx release groups.`);
    return unit.changed
      ? [
          {
            ...service,
            releaseName: unit.name,
            version: unit.version,
            candidateVersion: `${unit.version}-build.${manifest.sourceSha}`,
            sourceSha: manifest.sourceSha,
          },
        ]
      : [];
  });
}
if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const services = JSON.parse(readFileSync(process.argv[2], "utf8"));
  writeFileSync(".nx/releases/services.json", JSON.stringify(services));
  console.log(JSON.stringify({ include: releaseServices(readManifest(), services) }));
}
