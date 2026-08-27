import { describe, expect, it } from 'vitest';
import { authFailure } from './auth';

describe('authFailure', () => {
  it('classifica 401 como sessão inválida', () => expect(authFailure(401)).toBe('unauthorized'));
  it('classifica 403 como falta de permissão', () => expect(authFailure(403)).toBe('forbidden'));
  it('preserva outros erros', () => expect(authFailure(500)).toBe('other'));
});
