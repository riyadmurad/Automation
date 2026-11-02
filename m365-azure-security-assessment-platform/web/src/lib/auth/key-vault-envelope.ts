import { createCipheriv, createDecipheriv, randomBytes } from "node:crypto";
import { DefaultAzureCredential, ManagedIdentityCredential } from "@azure/identity";
import { CryptographyClient } from "@azure/keyvault-keys";
import type { AuthConfiguration } from "./config";

export type EncryptedEnvelope = {
  ciphertext: Buffer;
  nonce: Buffer;
  authenticationTag: Buffer;
  wrappedDataKey: Buffer;
  keyId: string;
  keyVersion: number;
};

export class KeyVaultEnvelopeProtector {
  private readonly credential;
  private readonly clients = new Map<number, CryptographyClient>();

  constructor(private readonly configuration: AuthConfiguration) {
    const clientId = process.env.AZURE_CLIENT_ID;
    this.credential = process.env.NODE_ENV === "production"
      ? new ManagedIdentityCredential(clientId ? { clientId } : {})
      : new DefaultAzureCredential({ managedIdentityClientId: clientId });
  }

  async protect(plaintext: Uint8Array, context: string): Promise<EncryptedEnvelope> {
    const dataKey = randomBytes(32);
    const nonce = randomBytes(12);
    const cipher = createCipheriv("aes-256-gcm", dataKey, nonce);
    cipher.setAAD(Buffer.from(context, "utf8"));
    const ciphertext = Buffer.concat([cipher.update(plaintext), cipher.final()]);
    try {
      const wrapped = await this.client(this.configuration.currentKeyVersion)
        .wrapKey("RSA-OAEP-256", dataKey);
      return {
        ciphertext,
        nonce,
        authenticationTag: cipher.getAuthTag(),
        wrappedDataKey: Buffer.from(wrapped.result),
        keyId: this.configuration.keyVersions.get(this.configuration.currentKeyVersion)!,
        keyVersion: this.configuration.currentKeyVersion,
      };
    } finally {
      dataKey.fill(0);
    }
  }

  async unprotect(envelope: EncryptedEnvelope, context: string): Promise<Buffer> {
    const expectedKeyId = this.configuration.keyVersions.get(envelope.keyVersion);
    if (!expectedKeyId || expectedKeyId !== envelope.keyId || envelope.nonce.length !== 12 ||
      envelope.authenticationTag.length !== 16 || envelope.ciphertext.length === 0 || envelope.wrappedDataKey.length === 0) {
      throw new Error("The encrypted record is unavailable or references an untrusted key version.");
    }

    const unwrapped = await this.client(envelope.keyVersion).unwrapKey("RSA-OAEP-256", envelope.wrappedDataKey);
    const dataKey = Buffer.from(unwrapped.result);
    try {
      if (dataKey.length !== 32) throw new Error("The wrapped data key has an invalid length.");
      const decipher = createDecipheriv("aes-256-gcm", dataKey, envelope.nonce);
      decipher.setAAD(Buffer.from(context, "utf8"));
      decipher.setAuthTag(envelope.authenticationTag);
      return Buffer.concat([decipher.update(envelope.ciphertext), decipher.final()]);
    } finally {
      dataKey.fill(0);
    }
  }

  private client(version: number): CryptographyClient {
    let client = this.clients.get(version);
    if (!client) {
      const keyId = this.configuration.keyVersions.get(version);
      if (!keyId) throw new Error("The configured session encryption key version is unavailable.");
      client = new CryptographyClient(keyId, this.credential);
      this.clients.set(version, client);
    }
    return client;
  }
}