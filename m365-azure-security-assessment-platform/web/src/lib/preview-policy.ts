export function isSyntheticPreviewEnabled(environment: {
  NODE_ENV?: string;
  ENABLE_SYNTHETIC_PREVIEW?: string;
}): boolean {
  return (
    environment.NODE_ENV !== "production" &&
    environment.ENABLE_SYNTHETIC_PREVIEW === "true"
  );
}