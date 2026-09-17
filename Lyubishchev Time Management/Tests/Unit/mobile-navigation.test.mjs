import test from 'node:test';
import assert from 'node:assert/strict';

import { isMoreSection, createMoreSheetController } from '../../wwwroot/js/mobile-navigation.mjs';

class FakeElement {
  constructor() {
    this.listeners = new Map();
    this.attributes = new Map();
    this.focusCount = 0;
  }

  addEventListener(type, handler) {
    if (!this.listeners.has(type)) this.listeners.set(type, []);
    this.listeners.get(type).push(handler);
  }

  dispatch(type, { target } = {}) {
    for (const handler of this.listeners.get(type) ?? []) {
      handler({ type, target: target ?? this });
    }
  }

  setAttribute(name, value) {
    this.attributes.set(name, String(value));
  }

  getAttribute(name) {
    return this.attributes.get(name);
  }

  focus() {
    this.focusCount += 1;
  }
}

class FakeDialog extends FakeElement {
  constructor() {
    super();
    this.open = false;
  }

  showModal() {
    this.open = true;
  }

  close() {
    if (!this.open) return;
    this.open = false;
    this.dispatch('close', { target: this });
  }
}

test('isMoreSection treats Category and Tag as More destinations, not Settings', () => {
  assert.equal(isMoreSection('category'), true);
  assert.equal(isMoreSection('tag'), true);
  assert.equal(isMoreSection('settings'), false);
});

test('opening the sheet shows the dialog, sets aria-expanded, and focuses the close control', () => {
  const trigger = new FakeElement();
  const dialog = new FakeDialog();
  const close = new FakeElement();
  const controller = createMoreSheetController({ trigger, dialog, close });

  controller.open();

  assert.equal(dialog.open, true);
  assert.equal(trigger.getAttribute('aria-expanded'), 'true');
  assert.equal(close.focusCount, 1);
});

test('clicking the backdrop closes the sheet, syncs aria-expanded, and restores focus to the trigger', () => {
  const trigger = new FakeElement();
  const dialog = new FakeDialog();
  const close = new FakeElement();
  createMoreSheetController({ trigger, dialog, close });

  dialog.showModal();
  dialog.dispatch('click', { target: dialog });

  assert.equal(dialog.open, false);
  assert.equal(trigger.getAttribute('aria-expanded'), 'false');
  assert.equal(trigger.focusCount, 1);
});

test('clicking inside the panel does not close the sheet', () => {
  const trigger = new FakeElement();
  const dialog = new FakeDialog();
  const close = new FakeElement();
  const panel = new FakeElement();
  createMoreSheetController({ trigger, dialog, close });

  dialog.showModal();
  dialog.dispatch('click', { target: panel });

  assert.equal(dialog.open, true);
});

test('clicking the close button closes the sheet', () => {
  const trigger = new FakeElement();
  const dialog = new FakeDialog();
  const close = new FakeElement();
  createMoreSheetController({ trigger, dialog, close });

  dialog.showModal();
  close.dispatch('click');

  assert.equal(dialog.open, false);
});
