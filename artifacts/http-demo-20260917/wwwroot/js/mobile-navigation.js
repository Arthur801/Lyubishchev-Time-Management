import { createMoreSheetController } from './mobile-navigation.mjs';

const trigger = document.querySelector('[data-more-trigger]');
const dialog = document.querySelector('[data-more-sheet]');
const close = document.querySelector('[data-more-close]');

if (trigger && dialog && close) {
  createMoreSheetController({ trigger, dialog, close });
}
