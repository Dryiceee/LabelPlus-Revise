declare class jamJSON {
    static parse(text: string, validate?: boolean, allowComments?: boolean): any;
    static stringify(value: any, space?: string | number, prefix?: string): string;
}
