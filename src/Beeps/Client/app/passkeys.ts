// Passkeys in the browser (PLAN 6.14). Each ceremony is two requests: the options the server wants the authenticator
// to have (JSON, with a ticket to send back), then what it answered, posted as a form. The options and the answer
// travel as WebAuthn's JSON forms, which the browser converts itself where it can, and this module does where it
// cannot yet (Safari before 18, Chrome before 129).
import { t } from './i18n.ts';

/** What the server's options handlers answer (PageModelExtensions.PasskeyOptions). */
interface OptionsAnswer<T> {
  options?: T;
  ticket?: string;
  error?: string;
}

/** The authenticator's answer, ready to post: the credential as JSON, and the ticket its options came with. */
export interface PasskeyAnswer {
  credential: string;
  ticket: string;
}

/** Why a ceremony stopped, to say so: the server's words, or the browser's failure. */
export class PasskeyError extends Error {}

/** The browser has passkeys at all (WebAuthn); without it, the buttons are not shown. */
export function passkeysSupported(): boolean {
  return typeof window.PublicKeyCredential === 'function' && typeof navigator.credentials?.create === 'function';
}

function fromBase64Url(text: string): ArrayBuffer {
  const base64 = text.replace(/-/g, '+').replace(/_/g, '/');
  const binary = atob(base64 + '='.repeat((4 - (base64.length % 4)) % 4));
  const bytes = new Uint8Array(binary.length);
  for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
  return bytes.buffer;
}

function toBase64Url(buffer: ArrayBuffer): string {
  let binary = '';
  for (const byte of new Uint8Array(buffer)) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

function descriptors(list: PublicKeyCredentialDescriptorJSON[] | undefined): PublicKeyCredentialDescriptor[] | undefined {
  return list?.map((item) => ({ type: 'public-key', id: fromBase64Url(item.id), transports: item.transports as AuthenticatorTransport[] | undefined }));
}

function creationOptions(json: PublicKeyCredentialCreationOptionsJSON): PublicKeyCredentialCreationOptions {
  if ('parseCreationOptionsFromJSON' in PublicKeyCredential) return PublicKeyCredential.parseCreationOptionsFromJSON(json);
  return {
    ...json,
    challenge: fromBase64Url(json.challenge),
    user: { ...json.user, id: fromBase64Url(json.user.id) },
    excludeCredentials: descriptors(json.excludeCredentials),
  } as PublicKeyCredentialCreationOptions;
}

function requestOptions(json: PublicKeyCredentialRequestOptionsJSON): PublicKeyCredentialRequestOptions {
  if ('parseRequestOptionsFromJSON' in PublicKeyCredential) return PublicKeyCredential.parseRequestOptionsFromJSON(json);
  return { ...json, challenge: fromBase64Url(json.challenge), allowCredentials: descriptors(json.allowCredentials) } as PublicKeyCredentialRequestOptions;
}

/** What extensions said: some password managers' credentials have no getClientExtensionResults. */
function extensionResults(credential: PublicKeyCredential): AuthenticationExtensionsClientOutputs {
  return typeof credential.getClientExtensionResults === 'function' ? credential.getClientExtensionResults() : {};
}

/**
 * The credential as WebAuthn's JSON form, which the server reads. A password manager that answers in the browser's
 * place may leave clientExtensionResults out of its toJSON(), which the server needs: it is always there.
 */
function credentialJson(credential: PublicKeyCredential): string {
  if (typeof credential.toJSON === 'function') {
    const json = credential.toJSON() as { clientExtensionResults?: unknown };
    return JSON.stringify({ ...json, clientExtensionResults: json.clientExtensionResults ?? extensionResults(credential) });
  }
  const response = credential.response;
  const common = { clientDataJSON: toBase64Url(response.clientDataJSON) };
  const answer = response instanceof AuthenticatorAttestationResponse
    ? { ...common, attestationObject: toBase64Url(response.attestationObject), transports: response.getTransports?.() ?? [] }
    : {
      ...common,
      authenticatorData: toBase64Url((response as AuthenticatorAssertionResponse).authenticatorData),
      signature: toBase64Url((response as AuthenticatorAssertionResponse).signature),
      userHandle: (response as AuthenticatorAssertionResponse).userHandle ? toBase64Url((response as AuthenticatorAssertionResponse).userHandle as ArrayBuffer) : undefined,
    };
  return JSON.stringify({
    id: credential.id,
    rawId: toBase64Url(credential.rawId),
    type: credential.type,
    authenticatorAttachment: credential.authenticatorAttachment ?? undefined,
    clientExtensionResults: extensionResults(credential),
    response: answer,
  });
}

async function askOptions<T>(url: string, body: FormData): Promise<{ options: T; ticket: string }> {
  let answer: OptionsAnswer<T>;
  try {
    const response = await fetch(url, { method: 'POST', body, credentials: 'same-origin', headers: { Accept: 'application/json' } });
    answer = (await response.json()) as OptionsAnswer<T>;
  } catch {
    throw new PasskeyError(t('Error.Offline'));
  }
  if (!answer.options || !answer.ticket) throw new PasskeyError(answer.error ?? t('Error.ActionFailed'));
  return { options: answer.options, ticket: answer.ticket };
}

/**
 * What the authenticator did with the options, as a credential; null when the person closed its dialog (or the
 * authenticator had nothing to offer), which needs no word.
 */
async function ceremony(run: () => Promise<Credential | null>): Promise<PublicKeyCredential | null> {
  try {
    const credential = await run();
    return credential instanceof PublicKeyCredential ? credential : null;
  } catch (error) {
    if (error instanceof DOMException && (error.name === 'NotAllowedError' || error.name === 'AbortError')) return null;
    // Asked to make a passkey it already has (the options name the account's own).
    if (error instanceof DOMException && error.name === 'InvalidStateError') throw new PasskeyError(t('Passkeys.AlreadyAdded'));
    throw new PasskeyError(t('Passkeys.Failed'));
  }
}

/** Makes a passkey: asks `url` for the options (posting `body`), and the authenticator for the passkey. */
export async function createPasskey(url: string, body: FormData): Promise<PasskeyAnswer | null> {
  const { options, ticket } = await askOptions<PublicKeyCredentialCreationOptionsJSON>(url, body);
  const credential = await ceremony(() => navigator.credentials.create({ publicKey: creationOptions(options) }));
  return credential ? { credential: credentialJson(credential), ticket } : null;
}

/** Signs with a passkey: asks `url` for the options (posting `body`), and the authenticator for its signature. */
export async function getPasskey(url: string, body: FormData): Promise<PasskeyAnswer | null> {
  const { options, ticket } = await askOptions<PublicKeyCredentialRequestOptionsJSON>(url, body);
  const credential = await ceremony(() => navigator.credentials.get({ publicKey: requestOptions(options) }));
  return credential ? { credential: credentialJson(credential), ticket } : null;
}
