import { TestBed } from '@angular/core/testing';
import { THEME_KEY, ThemeService } from './theme.service';

describe('ThemeService', () => {
  beforeEach(() => localStorage.removeItem(THEME_KEY));

  it('follows the system by default and cycles system -> light -> dark, remembering the choice', () => {
    const theme = TestBed.inject(ThemeService);
    expect(theme.preference()).toBe('system');

    theme.cycle();
    expect(theme.preference()).toBe('light');
    expect(theme.theme()).toBe('light');

    theme.cycle();
    expect(theme.theme()).toBe('dark');
    expect(localStorage.getItem(THEME_KEY)).toBe('dark');

    theme.cycle();
    expect(theme.preference()).toBe('system');
  });

  it('writes the theme on <html data-theme>', () => {
    const theme = TestBed.inject(ThemeService);
    theme.set('dark');
    TestBed.tick();

    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
  });
});
