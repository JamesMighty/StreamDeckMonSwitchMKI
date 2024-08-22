import { css, html, LitElement } from 'lit';
import { customElement } from 'lit/decorators.js';
import { keyed } from 'lit/directives/keyed.js';

import { Checkable, DataSourced, DynamicValueType, Gridded, Input, Item, Persisted } from '../mixins';
import { LocalizedMessage } from '../core';



function getByPath(obj: any, path: string) {
    console.log("getByPath: path: " + path + ", from obj: ", obj);

    var parts = path.split('.').filter(element => element);
    var o = obj;

    if (parts.length > 1) {
        for (var i = 0; i < parts.length - 1; i++) {
            if (!o[parts[i]])
                return [];
            o = o[parts[i]];
        }
    }

    return o[parts[parts.length - 1]] || []
}

function removeByPath(obj: any, path: string, value: string | number | boolean) {
    console.log("removeByPath: path: " + path + ", value: " + value + ", from obj: ", obj);
    var parts = path.split('.').filter(element => element);
    var o = obj;
    if (parts.length > 1) {
        for (var i = 0; i < parts.length - 1; i++) {
            if (!o[parts[i]])
                return;
            o = o[parts[i]];
        }
    }
    if (!o[parts[parts.length - 1]]) {
        console.log("no children")
        return;
    }
    o[parts[parts.length - 1]] = o[parts[parts.length - 1]].filter((element: string) => element != value);
    if (o[parts[parts.length - 1]].length == 0)
        delete o[parts[parts.length - 1]]
}

function setByPath(obj: any, path: string, value: string | number | boolean) {
    console.log("setByPath: path: " + path + ", value: " + value + ", to obj: ", obj);

    var parts = path.split('.').filter(element => element);
    var o = obj;
    var last_part = parts.length - 1

    if (last_part > 0) {
        for (var i = 0; i < last_part; i++) {
            if (!o[parts[i]])
                o[parts[i]] = {};
            o = o[parts[i]];
        }
    }

    o[parts[last_part]] = [];
    console.log("values before set: ", o[parts[last_part]])
    o[parts[last_part]].push(value);
    console.log("values after set: ", o[parts[last_part]])
}

@customElement('sdpi-checkbox-list-custom')
export class CheckboxListCustom extends Gridded(Persisted(Checkable(DataSourced(DynamicValueType(Input<typeof LitElement, Array<boolean | number | string>>(LitElement)))))) {
    /** @inheritdoc */
    public static get styles() {
        return [
            ...super.styles,
            css`
                .loading {
                    margin: 0;
                    padding: calc(var(--spacer) * 1.5) 0;
                    user-select: none;
                }
            `
        ];
    }

    _placeInGroup(group: { label: any; children?: Item[]; }, child: { groups: string; }) {
        if (child.groups) {
            child.groups += "." + group.label.toString();
        } else {
            child.groups = group.label.toString();
        }

        console.log("Placed child:", child, " to group", group);
        return child;
    }
    _get_is_checked(item: any) {
        var path = item.groups || "."
        var search = getByPath(this, 'value' + path);
        console.log("::_get_is_checked - search: ", search);
        if (Array.isArray(search)) {
            return search.findIndex((v) => v == item.value) > -1;
        }
        return search == item.value;
    }



    /** @inheritdoc */
    protected render() {
        return this.items.render({
            pending: () => html`<p class="loading">${this.loadingText}</p>`,
            complete: () =>
                this.renderGrid(
                    this.renderDataSource((item) => {
                        console.log("rendering item: ", item);
                        return this.renderCheckable(
                            'checkbox',
                            keyed(item.groups,
                                html`
                                    <input
                                        type="checkbox"
                                        .data-groups=${item.groups?item.groups:"."}
                                        .checked="${this._get_is_checked(item)}"
                                        .disabled=${this.disabled || item.disabled || false}
                                        .value=${item.value}
                                        @change=${this.handleChange}
                                />`
                            ),
                            item.label
                        )}
                    ,(group: any, children: any) => {
                        console.log("rendering group: ", group, " with children ", children)
                        var _a;
                        return html`<div class="group">${((_a = group.label) === null || _a === void 0 ? void 0 : _a.toString()) || ''}</div><hr/>${children.filter((child: { groups: string; }) => this._placeInGroup(group, child))}</div><br/>`;
                    })
                )
        });
    }

    /**
     * Handles a checkbox state changing.
     * @param ev The event data.
     */
    private handleChange(ev: HTMLInputEvent<HTMLInputElement>): void {
        const value = this.parseValue(ev.target.value);
        var this_mock = {
            "value": this.value as Object
        }
        if (value === undefined) {
            return;
        }
        console.log("Event target: ", ev.target, "dataset: ", ev.target.dataset, " item: ")

        const grouping = ev.target.parentElement?.getElementsByClassName("group")[0].innerHTML || ""
        if (ev.target.checked) {
            setByPath(this_mock, "value." + grouping, value);
        }
        else {
            removeByPath(this_mock, "value." + grouping, value);
        }

        this.value = JSON.parse(JSON.stringify(this_mock.value)); // trigger valuechanged event
        console.log("Values: ", this.value);
        return;
    }
}

declare global {
    interface HTMLElementTagNameMap {
        'sdpi-checkbox-list-custom': CheckboxListCustom;
    }
}
